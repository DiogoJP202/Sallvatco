<!-- markdownlint-disable MD013 MD060 -->

# Situação do projeto e passagem de contexto

Revisão documental: **01/10/2026**. Base de código: [`d347a68`](https://github.com/DiogoJP202/Sallvatco/commit/d347a68). Este relatório descreve arquivos versionados e evidências de testes; não certifica um ambiente produtivo nem substitui homologação externa.

## Resumo executivo

A vitrine está publicada no [GitHub Pages](https://diogojp202.github.io/Sallvatco/). O backend já possui contas, catálogo administrativo, imagens, estoque, carrinho, cupons, criação de pedidos e fluxos financeiros de teste. **A loja ainda não está pronta para vender em produção.** Pagamentos, reembolsos, recuperação financeira e cotação externa estão desligados na configuração versionada.

O estágio é de **desenvolvimento avançado do núcleo do MVP, com homologação e operação ainda pendentes**. Não usar percentual de conclusão: tarefas têm pesos diferentes e testes simulados não equivalem a operação real. Não declarar as fases 2–5 integralmente homologadas somente porque seus casos de uso principais existem.

## Como interpretar o estado

- **Implementado:** há código e testes, não necessariamente publicado em servidor.
- **Demonstração publicada:** somente HTML/CSS/JS/imagens no Pages.
- **Homologado:** exige execução registrada em Staging com integrações de teste reais; ainda não comprovada.
- **Planejado:** decisão ou requisito, sem entrega operacional correspondente.
- **Bloqueado:** depende de escolha, acesso ou informação ainda não fornecida.

## Situação por fase

| Fase | Entregue e evidência principal | Falta para o aceite integral |
|---|---|---|
| 0 — Planejamento | Documentos, requisitos `RF`/`RNF`, pendências `PBD` e ADR-001 a ADR-015. | Aprovação comercial e responsáveis/prazos das PBDs. |
| 1 — Fundação | Solution, camadas, Compose de banco local, Tailwind, logs, health, CI. | Validar instalação limpa no ambiente de destino; não confundir Compose local com deploy completo. |
| 2 — Contas | Identity Guid, confirmação, recuperação, perfil, endereços e autorização Admin. | E-mail real, primeiro Admin seguro, concessão/revogação, vínculo guest e histórico. |
| 3 — Catálogo | CRUD, variantes, imagens processadas, movimentos de estoque, SEO inicial e vitrine. | Aprovar/cadastrar catálogo real, fotos, preços e dados físicos; validar UX em Staging. |
| 4 — Carrinho/cupons | Carrinho guest/conta, merge, limpeza, cálculo e limites de cupons. | Aprovar regras comerciais e homologar jornadas integradas. |
| 5 — Pedidos | Snapshots, criação transacional, reserva, expiração, fila mínima e revisão de checkout. | Pós-compra guest durável e políticas; snapshot do prazo de preparo; homologação completa. |
| 6 — Frete | Cotação/revalidação Melhor Envio, cache, falhas tipadas e bloqueio de múltiplas unidades. | OAuth/refresh, caixa maior, serviços/regiões, `Shipment`, etiqueta, postagem e rastreio. |
| 7 — Pagamentos | Orders Sandbox, envio exclusivo, webhook, consulta, recuperação manual/job, reembolso total auditado. | Conta de teste em Staging, exceções financeiras, claims sem ID, políticas e implementação produtiva. |
| 8 — Operação | Fila mínima de pedidos e painel financeiro reaproveitáveis. | Fluxo pago → preparação → envio → entrega, comunicações, acompanhamento e resolução guiada. |
| 9 — Segurança/LGPD | Controles de conta, antiforgery, upload, auditoria e testes de abuso parciais. | Proxy/headers globais, revisão completa, procedimentos de titular, retenção e políticas aprovadas. |
| 10 — Infra/go-live | CI e publicação estática; arquitetura/runbooks planejados. | VPS/domínios, imagem OCI, Nginx/stacks, deploy, backups externos, restore, carga e aprovação. |
| 11 — Pós-MVP | Escopo documentado. | Não iniciar avaliações, fidelidade, marketing ou relatórios antes de priorização. |

O [backlog](BACKLOG.md) detalha execução; o [roadmap](ROADMAP.md) mantém critérios de aceite. Checkbox técnico concluído não encerra uma fase com homologação pendente.

## Publicado versus implementado

| Superfície | Situação |
|---|---|
| Home, Sobre, catálogo e quatro detalhes de perfumes | Exportados pelo `tools/Sallvat.Showcase`; filtros, galeria, variantes e diálogo demonstrativo. |
| `/linha-corporal` | Nove apresentações editoriais de body splash; não é cadastro vendável no banco. |
| `/conta/*`, `/carrinho`, `/checkout`, `/pagamentos/*` | Exigem ASP.NET e banco; não funcionam como loja no Pages. |
| `/Admin/Produtos`, `/Admin/Cupons`, `/Admin/Pedidos`, `/Admin/Pagamentos` | Servidor, autenticação e autorização; não exportados. |
| `/integracoes/mercado-pago/webhook` | POST no servidor, desligado por configuração; não existe como receptor no Pages. |
| `/health/live`, `/health/ready` | Processo e conexão PostgreSQL, respectivamente; não confirmam saúde de todos os serviços externos. |
| Contato, privacidade, termos, trocas, entrega, sitemap produtivo | Escopo planejado, ainda não entregue como conjunto institucional final. |

O nome exibido é **Lumiere**, mas o endereço legado continua `/perfumes/cumiere/`. Não renomear URLs sem redirect/compatibilidade. Os perfumes do exportador ainda têm preços de demonstração (`299,90`/`449,90`), diferentes da tabela enviada pela marca (`30 ml: 45,00`, `50 ml: 70,00`, `100 ml: 125,00`). Essa divergência precisa ser resolvida com aprovação, não interpretada como preço final. A linha corporal apresenta `200 ml / R$ 45,00`, a partir do material recebido, em catálogo editorial estático. Notas, duração e disponibilidade demonstrativas não são comprovação comercial.

As imagens tratadas, sua origem e seus limites estão em [IMAGES.md](IMAGES.md). Remover avisos da vitrine não substitui a aprovação dos materiais para uso comercial.

## Mapa dos arquivos para continuidade

| Caminho | Responsabilidade |
|---|---|
| `src/Sallvat.Domain` | Entidades, estados e invariantes; sem EF/MVC/provedor. |
| `src/Sallvat.Application` | Contratos, DTOs, cálculos e validação de entrada. |
| `src/Sallvat.Infrastructure` | Casos de uso persistentes, Identity, EF, storage e adapters externos. |
| `src/Sallvat.Web/Program.cs` | Composition root, opções, middleware, autenticação e jobs. |
| `src/Sallvat.Web/Controllers` e `Areas/Admin` | Rotas, formulários, autorização e views. |
| `src/Sallvat.Web/Styles`, `wwwroot/css`, `wwwroot/js` | Fontes Tailwind, CSS gerado e interações nativas. |
| `src/Sallvat.Web/Models/Catalog/BodySplashPresentation.cs` | Dados editoriais da linha corporal, fora das entidades comerciais. |
| `tools/Sallvat.Showcase` | Perfumes demonstrativos, exportação e validação de links/assets. |
| `tests/Sallvat.UnitTests`, `tests/Sallvat.IntegrationTests` | Domínio, arquitetura, MVC, serviços, HTTP fake e PostgreSQL efêmero. |
| `.github/workflows/ci.yml`, `pages.yml` | Validação e publicação estática independentes. |
| `compose.yaml` | Apenas PostgreSQL de Development; não há Dockerfile da aplicação nem stack produtiva. |
| `.local/` | Saídas locais ignoradas: e-mails, chaves, imagens e validações; nunca publicar. |

## Banco e limites financeiros

Existem **13 migrations** em `src/Sallvat.Infrastructure/Persistence/Migrations`, da `20260902133721_InitialIdentityAndCustomers` à `20260930183351_AddPaymentRefundDispatch`. O snapshot EF é a descrição física vigente. `Shipment` e `ShipmentStatus` permanecem planejados; o frete selecionado já é salvo no próprio `Order`. `CustomerId` pode ser nulo no pedido guest, que guarda os dados do comprador em snapshot; não existe criação automática de conta nem vínculo por e-mail não confirmado.

Captura confirmada consome reserva/estoque uma vez. Expiração libera reserva/cupom segundo as guardas locais. Aprovação tardia ou divergência vai para revisão, sem reabrir pedido ou reservar de novo silenciosamente. Reembolso confirmado não repõe estoque/cupom. O fluxo suportado de reembolso é **total e Sandbox**, não parcial.

Envio financeiro registra posse antes do HTTP. Interrupção entre posse e resposta pode deixar resultado incerto, inclusive sem ID externo. Isso **não autoriza repetir POST, apagar posse, trocar chave ou editar status no banco**. Com ID conhecido, há consultas controladas; sem ID, falta procedimento implementado de associação/resolução com evidência. Produção é recusada pelo validador atual do Mercado Pago: não basta trocar token ou flag.

Migrations são explícitas. Downgrades financeiros podem recusar perda de evidências; uma tentativa de downgrade pode já ter removido extensões vazias anteriores à recusa. Não usar downgrade como procedimento rotineiro. Ver [DEPLOYMENT.md](DEPLOYMENT.md) e [OPERATIONS.md](OPERATIONS.md).

## Pendências comerciais completas

A fonte autoritativa continua sendo [REQUIREMENTS.md](REQUIREMENTS.md#pending-business-decisions). Não há responsáveis nominais ou datas aprovados; precisam ser atribuídos pelo negócio e pelo responsável técnico.

| PBD | O que ainda precisa ser decidido/validado |
|---|---|
| 001 | Dados legais, endereço completo, domínio, contatos oficiais e responsável LGPD. |
| 002 | Aprovação final de identidade, textos, fotografias/packshots, nomes, notas, preços, volumes e estoque real. |
| 003 | Necessidade fiscal/logística de CPF e processo fiscal; não coletado hoje. |
| 004 | Meios de pagamento, parcelas, juros, boleto e compatibilidade da conta de teste. |
| 005 | Reserva definitiva (30 min é padrão técnico) e operação para pagamento tardio. |
| 006 | Cancelamento, troca, devolução, reembolso parcial, prazos e devolução física. |
| 007 | Caixa maior: medidas, peso real e capacidade; origem completa, transportadoras/serviços/regiões e calendário/corte de preparo. |
| 008 | Frete grátis, retirada e promoções de frete; nenhuma regra aprovada. |
| 009 | Cupons: acúmulo, limites, validade, elegibilidade e frete. |
| 010 | Provedor, domínio remetente e modelos transacionais; Gmail de contato não é serviço de envio. |
| 011 | Acompanhamento guest por link seguro; retorno atual depende da sessão original e não substitui esse acesso. |
| 012 | Retenção, anonimização, atendimento aos titulares e revisão jurídica. |
| 013 | Analytics, pixels, cookies opcionais, newsletter e double opt-in. |
| 014 | Destino externo, retenção definitiva, responsáveis e testes de backup/restore. |
| 015 | VPS/capacidade, staging/produção, domínios, DNS e Cloudflare. |
| 016 | Administradores individuais, provisionamento, concessão e revogação. |
| 017 | Instagram como link/embed e canal oficial de suporte. |

Confirmado: contato `sallvatco@gmail.com`, conta Melhor Envio existente, origem `02320-040`, média de **2 dias úteis para preparar e postar**, perfume embalado `130 g / C11 × L16 × A5 cm` para todos os volumes informados, body splash 200 ml embalado `300 g / C16 × L20 × A7 cm`, vários itens em caixa maior. Estas informações não equivalem a credenciais configuradas ou frete homologado. O bloqueio de várias unidades permanece.

## Ordem recomendada para continuar

1. Aprovar este estado e atribuir as decisões bloqueadoras; obter VPS/domínio e estratégia de e-mail/acesso sem transmitir segredos no chat.
2. Preparar artefatos de Staging: imagem, proxy confiável, HTTPS/headers, isolamento, volumes, secrets, Admin inicial e e-mail real de teste. Ainda há implementação necessária, não apenas configuração.
3. Aplicar migrations explicitamente em banco de testes, registrar backup/restore e validar conta/catálogo/checkout sem integrações financeiras ativas.
4. Homologar uma unidade no Melhor Envio e Orders Mercado Pago com contas de teste, webhook e exceções. Registrar evidências em [OPERATIONS.md](OPERATIONS.md); não ativar produção.
5. Completar embalagem multi-item, logística, acompanhamento/e-mails e resolução financeira; atualizar backlog e testes a cada incremento.
6. Concluir hardening, conteúdo/políticas, LGPD, carga e recuperação; somente então aprovar go-live e compra produtiva controlada separadamente.

## Revisão documental desta entrega

Foram confrontados README/índice/roadmap/backlog com controllers, composição de serviços, opções, entidades/migrations, testes e workflows. Corrigidos estados antigos, alegações de fases concluídas, regras de retry financeiro, health check e distinção entre infraestrutura planejada e existente. ADRs históricos foram preservados, com estado atual explicitado. Não houve auditoria exaustiva de cada linha nem certificação de segurança, jurídica ou das APIs externas.

Referência de teste da base `d347a68`: **576 testes** no CI (66 unitários e 510 de integração, incluindo 10 PostgreSQL), [CI](https://github.com/DiogoJP202/Sallvatco/actions/runs/36761223350) e [Pages](https://github.com/DiogoJP202/Sallvatco/actions/runs/36761223383). Contagens e runs são evidências datadas, não garantias permanentes. O relatório final desta revisão e os workflows do commit documental registram sua validação, sem inventar teste de Staging, Lighthouse ou compra real.

Validação local da revisão em 01/10/2026: build Release com zero avisos/erros; 66 unitários e 500 de integração aprovados, 10 PostgreSQL pulados por ausência de conexão de testes; Tailwind, Markdown lint e formatação sem alterações funcionais. Exportação para `.local/docs-review-20261001` concluída com validação interna de assets/rotas. Índice, links/âncoras relativos, sequência ADR-001–015, epics F0–F11 e contagem de migrations conferidos. Diagramas foram revisados em texto e atualizados para Orders/entidades financeiras; não houve renderização Mermaid ou nova auditoria visual/Lighthouse nesta entrega. Nenhuma migration foi aplicada a banco operacional, nenhuma credencial foi provisionada e nenhuma integração foi ativada.
