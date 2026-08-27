<!-- markdownlint-disable MD013 MD060 -->

# Deploy, rollback e restauração

## Princípios

- o mesmo artefato imutável aprovado em Staging é promovido para Production;
- secrets nunca entram na imagem;
- migration é etapa explícita e observável;
- backup precede mudança de schema produtiva;
- deploy só termina após health check e smoke test;
- rollback de imagem só é permitido se o schema continuar compatível.

## Artefatos futuros

- imagem OCI de `sallvat-web` identificada por commit SHA e digest;
- assets Tailwind compilados em build reprodutível;
- arquivos Compose e configuração Nginx versionados quando a Fase 1 começar;
- migrations dentro do assembly de `Infrastructure`;
- checklist/release notes com mudanças e decisões operacionais.

## Pipeline futuro

GitHub Actions deverá:

1. restaurar dependências com lock files;
2. compilar em Release;
3. executar testes unitários e de integração;
4. verificar assets e documentação;
5. gerar imagem sem secrets;
6. analisar vulnerabilidades conforme ferramenta adotada;
7. publicar em registry privado/GHCR com SHA;
8. promover somente artefato aprovado.

Credenciais de deploy têm escopo mínimo e não são expostas a pull requests não confiáveis.

## Deploy em Staging

1. confirmar backup/estado da stack quando houver dados relevantes;
2. obter a imagem pelo digest;
3. validar variáveis obrigatórias sem exibir valores;
4. iniciar container de migration ou executar comando explícito;
5. iniciar/atualizar web;
6. aguardar `/health/ready`;
7. executar smoke tests de home, catálogo, autenticação, banco e integrações sandbox;
8. validar logs, headers, `noindex` e Cloudflare Access;
9. registrar versão homologada.

## Deploy em Production

1. aprovar versão já homologada;
2. anunciar/manter janela se houver risco de indisponibilidade;
3. verificar CPU, memória, disco, backup job e saúde atuais;
4. criar backup pré-deploy e validar checksum/upload;
5. baixar imagem por digest;
6. inspecionar plano da migration e compatibilidade de rollback;
7. aplicar migration explicitamente;
8. atualizar o serviço web sem alterar PostgreSQL ou volumes;
9. aguardar readiness;
10. executar smoke tests públicos e administrativos seguros;
11. conferir erros, webhooks e filas/jobs pendentes;
12. registrar versão, operador e resultado.

## Migrations

- nunca executar automaticamente no startup;
- não misturar alteração destrutiva com código que ainda depende da coluna antiga;
- usar expand-and-contract: adicionar, migrar/validar dados, alternar aplicação e remover em release posterior;
- índices grandes são planejados para evitar lock prolongado;
- falha interrompe o deploy antes de promover web;
- correção usa nova migration; não editar migration já aplicada.

## Rollback

### Aplicação

1. confirmar que migration é backward-compatible;
2. selecionar digest anterior conhecido;
3. atualizar apenas web;
4. aguardar health e executar smoke;
5. registrar causa e versão;
6. manter dados criados pela versão nova, salvo procedimento aprovado.

### Schema

Rollback destrutivo de banco não é padrão. Quando indispensável, parar escrita, preservar dump atual, restaurar backup em nova instância/volume, validar consistência e só então alternar. A decisão exige análise de perda de dados e autorização explícita.

## Restore

1. criar ambiente isolado e volumes vazios;
2. verificar checksum, descriptografar cópia e registrar quem acessou;
3. restaurar PostgreSQL na versão compatível;
4. restaurar imagens e Data Protection keys quando necessário;
5. aplicar somente migrations previstas para a versão alvo;
6. validar contagens, constraints, amostras de pedido/imagem e login técnico;
7. executar testes críticos;
8. medir RTO/RPO alcançados e destruir dados de teste com segurança.

Restore produtivo nunca sobrescreve volume original antes da validação.

## Primeiro provisionamento

- aplicar patches no Ubuntu e criar usuário administrativo sem login root por senha;
- configurar firewall, Docker, rotação e sincronização de tempo;
- criar diretórios/volumes com permissões mínimas;
- configurar Cloudflare e certificado de origem;
- criar stacks isoladas e usuários PostgreSQL;
- instalar secrets por ambiente;
- executar migrations e criar conta Admin pelo procedimento seguro;
- realizar compra de homologação controlada antes de abrir tráfego;
- confirmar backup e restore.

## Critérios de go-live

- `PBD-001`, `PBD-003`, `PBD-004`, `PBD-006`, `PBD-007`, `PBD-010`, `PBD-012`, `PBD-014`, `PBD-015` e `PBD-016` resolvidas;
- fluxos críticos aprovados em Staging;
- headers, TLS, autorização, webhook, rate limits e uploads testados;
- restore executado com sucesso;
- políticas publicadas e coerentes;
- runbook e acesso de suporte disponíveis.
