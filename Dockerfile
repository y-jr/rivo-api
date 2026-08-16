# Build em duas fases: o SDK só existe para compilar; a imagem final leva
# apenas o runtime, o que a torna bastante mais pequena e reduz a superfície
# de ataque.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source

# Copia-se `src/` inteiro antes do restore, em vez de listar cada .csproj.
#
# Listar os projectos um a um preservaria a cache do restore entre alterações
# de código, mas obriga a editar este ficheiro sempre que nasce um módulo — e
# o esquecimento só aparece como falha de build. Num monólito modular que vai
# crescer para catorze módulos, a correcção vale mais do que os segundos de
# cache.
# A gestão central de versões vive na raiz e aplica-se a tudo o que está
# abaixo. Sem ela na imagem, o restore não sabe que versão de cada pacote usar
# e falha — os `.csproj` declaram o pacote, não a versão.
COPY Directory.Packages.props ./

COPY src/ src/

# Restaura a partir do projecto da API, e **não** da solução.
#
# A solução inclui os projectos de teste (ADR-022), que vivem em `tests/` e não
# entram nesta imagem: ela serve para correr a API, não para a testar — disso
# encarrega-se o job 1 do CI (ADR-023). Restaurar pela solução obrigaria a
# copiar `tests/` para cá só para o restore passar, e a arrastar os pacotes de
# teste para dentro de uma imagem de produção.
#
# `src/Rivo.Api` puxa transitivamente todos os módulos, por referência de
# projecto — nenhum módulo fica por restaurar.
RUN dotnet restore src/Rivo.Api
RUN dotnet publish src/Rivo.Api -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app .

# Raiz do armazenamento de documentos, criada na imagem com o dono certo.
#
# Tem de ser feito aqui, e não em runtime: um volume nomeado herda dono e
# permissões do directório que existe na imagem no ponto de montagem. Sem
# isto, o volume nasce pertencente a root e a aplicação — que corre como
# utilizador sem privilégios — não consegue escrever.
RUN mkdir -p /var/rivo/documents && chown -R $APP_UID:$APP_UID /var/rivo

# Utilizador não-root fornecido pela imagem base. Um processo comprometido
# fica sem privilégios administrativos dentro do container.
USER $APP_UID

# 8080 é o porto por omissão das imagens .NET desde a versão 8, precisamente
# por não exigir privilégios de root.
EXPOSE 8080

ENTRYPOINT ["dotnet", "Rivo.Api.dll"]
