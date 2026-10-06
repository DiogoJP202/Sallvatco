<!-- markdownlint-disable MD013 MD060 -->

# Desenvolvimento, revisão e commits

## Começar e retomar trabalho

Ler [STATUS.md](STATUS.md), [BACKLOG.md](BACKLOG.md) e o documento do módulo antes de mudar comportamento. Verificar `git status --short`, branch, últimos commits e instruções locais aplicáveis. Alterações preexistentes pertencem ao autor; não restaurar/apagar arquivos alheios. O fluxo atual autorizado usa `main` e commits pequenos, sem force push. Uma mudança de política de branches deve ser acordada, não presumida.

Pré-requisitos e comandos são definidos por `global.json`, `.nvmrc`, `package.json`, `dotnet-tools.json` e lock files. O [README](../README.md) contém o setup de banco/user-secrets. Não há seed de produção nem conta Admin padrão. Testes usam fixtures próprias; o exportador cria somente dados descartáveis.

## Validação local reproduzível

Executar na raiz, com SDK/Node compatíveis:

```powershell
dotnet tool restore
npm ci --ignore-scripts
dotnet restore Sallvat.sln --locked-mode
npm run css:build
npm run lint:markdown
dotnet build Sallvat.sln -c Release --no-restore
dotnet test Sallvat.sln -c Release --no-build
dotnet format Sallvat.sln --verify-no-changes --no-restore
git diff --check
```

Se formatar intencionalmente, usar `dotnet format Sallvat.sln --no-restore` e revisar o diff. CSS compilado é versionado; mudança de view/style requer reconstrução e inspeção de `wwwroot/css/app.css`. Não editar o CSS minificado manualmente. Não atualizar lock files/pacotes sem necessidade da tarefa.

Os testes `[PostgreSqlFact]` são pulados sem `SALLVAT_TEST_POSTGRES`. Isso deve aparecer no relato: sucesso local com skips **não** comprova constraints/concorrência reais. O CI fornece PostgreSQL isolado e executa esses cenários. Para executá-los localmente, fornecer conexão de um servidor **exclusivo de testes**, com permissão para criar/remover bancos efêmeros `sallvat_payment_tests_*`; nunca apontar para servidor produtivo. Não imprimir a variável nem versionar a senha.

## Nova migration

Criar apenas para uma alteração de modelo solicitada e com configuração de Development apontando para dados locais:

```powershell
dotnet ef migrations add NomeDaMudanca --project src/Sallvat.Infrastructure --startup-project src/Sallvat.Web --output-dir Persistence/Migrations -- --environment Development
```

Revisar `Up`, `Down`, designer, snapshot, dados legados e constraints; testar upgrade em banco efêmero e downgrade permitido/recusado. Não aplicar automaticamente a bancos operacionais. Incluir plano de compatibilidade no commit.

## Exportar a apresentação

```powershell
dotnet run --project tools/Sallvat.Showcase/Sallvat.Showcase.csproj -c Release -- --output .local/showcase-review --site-url https://diogojp202.github.io/Sallvatco/
```

O exportador valida rotas/assets/links internos. Usar diretório de saída descartável, separado de fontes e dados. O `--site-url` inclui o subcaminho do projeto. Não copiar configuração, segredos, e-mails, banco ou área Admin para o Pages. GitHub Pages deve usar **GitHub Actions** como source.

Em alterações visuais: revisar 390×844, 768×1024 e 1440×1000, teclado, foco, menu, filtros, variantes, galeria, Escape no diálogo, console e recursos. Auditar acessibilidade/boas práticas ≥95 e desempenho ≥85 como metas, registrando ferramenta/URL/data/resultados; não declarar essas notas sem medição. Requisição HTTP 200 não substitui teste visual.

## Atualizar documentação junto da mudança

- Estado e limitações: `STATUS.md` e resumo do README.
- Trabalho e aceite: `BACKLOG.md` e `ROADMAP.md`.
- Regra/contrato: documento do módulo, `REQUIREMENTS.md` quando aplicável.
- Schema: `DATABASE.md`, migration e plano de compatibilidade.
- Configuração/operação: `CONFIGURATION.md`, `DEPLOYMENT.md` e `OPERATIONS.md`.
- Testes: `TESTING.md`, cenários reais executados e limitações.
- Decisão arquitetural: atualizar ADR ou criar próximo número sem renumerar históricos.
- Links: relativos para arquivos do repositório; verificar destinos e âncoras alterados.

Texto de intenção deve dizer “planejado”; registros de incrementos antigos não devem contradizer silenciosamente o resumo vigente. Não preencher PBDs por inferência. `docs/README.md` deve indexar todo documento de primeiro nível.

## Checklist antes de commit

1. Confirmar escopo e ausência de efeitos externos não autorizados.
2. Revisar `git diff --stat` e `git diff`; conferir arquivos novos e removidos.
3. Executar testes/lints proporcionais, sempre registrando falhas/skips.
4. Verificar que `.env`, user-secrets, tokens, chaves, outbox, dados pessoais, exports e resultados locais não entraram no staging.
5. Adicionar **caminhos explícitos** com `git add`, evitando incluir trabalho alheio.
6. Revisar `git diff --cached --stat`, `git diff --cached` e `git diff --cached --check`.
7. Criar mensagem curta: `docs: ...`, `feat: ...`, `fix: ...` ou `test: ...`; separar correção funcional de documentação quando possível.
8. Conferir `git status --short` e `git log -1 --oneline`.

## Push e aceite da etapa

```powershell
git push origin main
```

Desde 06/10/2026, o push inicia o CI, cujo job `pages` depende de `validate` com sucesso. O workflow Pages é reutilizável (`workflow_call`), sem gatilho independente. Validação e exportação fazem checkout de `github.sha`; a chamada relativa reutiliza o arquivo do mesmo commit. Falha/cancelamento da validação impede exportação/deploy. Pull requests só validam; publicação exige `main` e evento `push` ou `workflow_dispatch`. Não há `pull_request_target`, herança de secrets ou checkout de branch mutável.

Na interface Actions, acompanhar **CI → Build, test and lint → Publish validated showcase / Export static showcase → Deploy static showcase**. Não esperar um segundo run chamado GitHub Pages. Para republicar manualmente, executar **CI / Run workflow / main**, que repete todas as validações. Não reexecutar workflows de commits anteriores à proteção para contorná-la; reaplicar uma mudança em novo commit é o caminho seguro.

Execuções de `main` não são canceladas automaticamente por outro push enquanto publicam; o grupo de concorrência serializa o trabalho ativo, podendo substituir uma execução ainda pendente por uma mais nova. Não há promessa de publicar todo commit intermediário. Pull requests mantêm cancelamento de execuções substituídas. O grupo de publicação tem nome diferente do grupo do CI, evitando autocancelamento entre chamador e workflow reutilizado.

Se falhar: investigar, corrigir, validar e criar novo commit normal; nunca force push ou apagar histórico para esconder falha. Validação verde isolada ainda não conclui publicação: exportação/deploy também precisam passar no mesmo SHA. O job de validação recebe apenas leitura; escrita Pages/OIDC fica restrita ao caminho de publicação, e o job de exportação usa leitura de Pages.

Depois do job de deploy verde, conferir home, catálogo, quatro detalhes, Sobre e linha corporal no endereço publicado. Mudanças somente documentais ainda acionam o pipeline. O Pages não é deploy do backend.

## Relato da entrega

Registrar: data, escopo, SHA, arquivos principais, testes executados/passados/pulados, links de CI/Pages, mudanças de configuração/migration, riscos e próximo passo. Informar explicitamente quando não houve teste com provedor, navegador, banco local ou Staging. Não marcar fase pronta enquanto faltar critério de aceite; não apresentar teste fake como transação real.
