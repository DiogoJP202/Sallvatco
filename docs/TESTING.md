<!-- markdownlint-disable MD013 MD060 -->

# Estratégia de testes

## Gate de publicação — 06/10/2026

`ContinuousIntegrationWorkflowTests` verifica estruturalmente dependência `pages → validate`, Pages somente reutilizável, guards de evento/branch, ausência de bypass por `always()`, checkout pelo mesmo SHA, permissões separadas, revalidação manual e concorrência sem cancelar automaticamente `main`. Os testes não simulam o scheduler GitHub; a execução no remoto comprova o caminho de sucesso e a ordem dos jobs. Não se introduz falha intencional em `main` apenas para testar o bloqueio.

Critério da entrega: validação e jobs de exportação/deploy verdes no mesmo run CI; PR/manual fora de `main` não podem alcançar publicação pelas condições versionadas. Validar sintaxe YAML, formatação, suíte completa e páginas públicas depois do deploy. O comportamento operacional está em [DEVELOPMENT.md](DEVELOPMENT.md#push-e-aceite-da-etapa).

### Bloqueio real e correção de dependências

O [run 37521653797](https://github.com/DiogoJP202/Sallvatco/actions/runs/37521653797), commit `24a0d57`, falhou legitimamente em `npm audit --audit-level=high`; a publicação foi pulada. É evidência real do bloqueio por falha, não teste intencional nem publicação concluída. O histórico foi preservado e a correção usa novo commit, sem diminuir o nível da auditoria.

Após a correção de tooling em 06/10: instalação limpa `npm ci --ignore-scripts`, auditoria npm com zero vulnerabilidades reportadas, 26 documentos sem erros e CSS gerado sem diferença. O runner tem sete testes: escopo/ordenação, violação com diagnóstico, exceções inline, status de sucesso, README ausente, docs ausente e parsing de fórmulas/tabelas/blocos Mermaid. Um oitavo teste verifica notificação do watcher nativo, usado pelo Tailwind. Esses testes rodam em `npm run test:tooling` no CI; não validam a sintaxe do conteúdo de diagramas Mermaid.

Suíte .NET local: **70 unitários + 500 de integração aprovados; 10 PostgreSQL pulados**, total de 580 casos. Build Release com zero avisos/erros e formatação sem diferenças. O CI ainda precisa executar os dez casos com banco real e concluir publicação para satisfazer o aceite deste incremento. Nenhum teste usa credenciais financeiras ou libera integrações produtivas.

## Evidência e execução — revisão de 01/10/2026

Base `d347a68`: CI registrou 66 testes unitários e 510 de integração (576 aprovados), incluindo 10 cenários em PostgreSQL efêmero. Referências de runs em [STATUS.md](STATUS.md). Sem `SALLVAT_TEST_POSTGRES`, esses dez cenários são pulados localmente; não descrevê-los como aprovados nessa execução. Comandos e cuidados do servidor descartável em [DEVELOPMENT.md](DEVELOPMENT.md).

Testes HTTP usam provedores simulados. Não demonstram conta Mercado Pago homologada, cotação real, e-mail entregue, VPS configurada, restore operacional, conformidade jurídica ou nota Lighthouse. A revisão documental não substitui essas validações. [OPERATIONS.md](OPERATIONS.md) contém a matriz e o registro ainda a preencher em Staging.

Para documentação: executar Markdown lint, verificar links/âncoras, índice completo, numeração única dos ADRs, coerência de backlog/status e `git diff --check`. Para alterações no comportamento, executar build/test/formatação e cenários específicos, conforme a definição de pronto. Diagramas conceituais devem ser identificados como alvo quando incluírem componentes não implementados.

## Objetivo

Testar riscos de negócio, segurança e integração, não perseguir 100% de cobertura. Uma regra crítica deve ter teste rápido quando possível e teste integrado quando depende de transação, banco, autorização ou pipeline HTTP.

## Envio e confirmação de reembolso — 30/09/2026

`PaymentRefundFlowTests` valida posse antes do HTTP, chave estável, nenhum reenvio, revalidação de versões/Admin, concorrência, timeout, navegador desconectado, confirmação manual idempotente, webhook durante POST sem overwrite, captura/valor divergentes, intenção não enviada e pedido alterado. `MercadoPagoRefundGatewayTests` confere host/path fixos, corpo vazio, header idempotente, flags/Sandbox, resposta inválida, 401/409/429/500 e cancelamento sem retry. `MercadoPagoOrderQueryTests` cobre devolução única integral ligada à captura, estados inconsistentes, parcialidade, múltiplas devoluções, IDs inválidos, contestação e transação desconhecida.

O fluxo HTTP Admin em `PaymentRefundWebTests` exercita formulários distintos, permissão, revogação, antiforgery, confirmação obrigatória, GET sem efeito, flag desligada, fila pendente, `no-store` e resultado final sanitizado. `RefundDispatchAndConfirmationAreAtomicExclusiveAndRecoverableWithoutRepost` roda em PostgreSQL isolado no CI: falha da auditoria de claim impede POST; seis conexões enviam uma vez; falha da auditoria de confirmação desfaz todos os efeitos; consulta e webhook concorrentes confirmam sem duplicidade; downgrade com evidência é recusado. Nenhum teste usa conta real ou habilita operação produtiva; homologação externa continua obrigatória.

## Preparação local de reembolso — 30/09/2026

`PaymentRefundPreparationTests` cobre snapshot imutável, replay com motivo diferente, auditoria sanitizada, valor capturado, versões, ausência de efeitos financeiros/estoque, flag/configuração inválida, Admin revogado/desconhecido, motivo/correlation ID inválidos, relógio anterior, captura pendente/em revisão/produtiva/divergente, falta de ID/confirmação, pedido cancelado/reembolsado, cancelamento e contexto com alterações pendentes. Oito chamadas simultâneas InMemory produzem uma intenção e uma auditoria; consultas administrativas não deixam entidades rastreadas.

`PaymentRefundWebTests` atravessa MVC, antiforgery e DI: confirmação explícita, ausência de campos obrigatórios, tentativa de impor valor/ator pelo formulário, replay, fila, `no-store`, ocultação quando desativado, acesso Customer/revogação de Admin, versão antiga, GET incapaz de mutar e limite de cinco POSTs/minuto. Não há chamada ao gateway durante preparação ou leitura.

`RefundIntentIsAtomicUniqueQueryableAndCannotBeDroppedWithHistory` usa PostgreSQL descartável no CI: falha forçada na auditoria desfaz toda a intenção, seis conexões disputam um registro, unicidade e constraint monetária são impostas pelo banco, fila/detalhe traduzem para SQL e downgrade com histórico é recusado. O teste legado de recuperação reaplica o schema atual após ensaiar downgrade: migrations posteriores vazias podem ter sido revertidas antes de uma proteção mais antiga recusar o restante. Migrations não são aplicadas no banco operacional. Estes testes não comprovam devolução de dinheiro: dispatcher, confirmação externa e homologação continuam pendentes.

## Checkout e retorno — 30/09/2026

`CheckoutPaymentFlowTests` atravessa controllers, antiforgery, Data Protection, serviços e persistência InMemory com gateways de pagamento/frete simulados. Cobre criação após revisão, replay sem duplicidade, segunda confirmação, redirecionamento reutilizado sem novo POST externo, confirmação por webhook canônico e estoque consumido. Retornos de sucesso/pendência/falha com `status=approved` falso permanecem pendentes; GET não consulta o provedor. Confere `no-store`, `noindex` e ausência de dados pessoais na página de situação.

Negativos: token adulterado, expirado e usado por outro cliente; checkbox/antiforgery ausentes; conta alheia e sessão guest vencida; falta de localizador; preço/quantidade/cupom/frete/prazo/transportadora alterados; frete indisponível; resultado de envio incerto sem reenvio; flags desativadas, proteção Sandbox incompleta, frete produtivo e limite compartilhado de cinco requisições/minuto. O teste do gateway verifica que as três URLs recebem somente o localizador server-side da tentativa.

`ReviewedOrderChecksTermsAndConcurrentSubmissionsReserveOnlyOnce` usa PostgreSQL isolado no CI: revisão divergente não grava efeitos e seis criações concorrentes para a mesma tentativa deixam um pedido, item, reserva, movimento e cliente guest, mantendo replay idempotente. Não usa banco operacional nem credenciais reais. Homologação no provedor e revisão visual em Staging continuam obrigatórias; testes simulados não comprovam funcionamento da conta ou transporte HTTP externo real.

## Execuções duráveis de recuperação — 29/09/2026

`AutomaticPaymentRecoveryTests` cobre configuração desligada, dependência das flags, limite de lote, intervalos e orçamento persistido entre instâncias, captura com evidência de sistema sem conta Admin/recibo inventados, recuperação manual após orçamento, concorrência com outro worker/manual, reinício após cancelamento, resposta antiga após substituição/webhook, exclusão de tentativa antiga/sem ID/em revisão e aprovação tardia de pedido cancelado. `PaymentRecoveryWebTests` verifica origem/contador/resultado no HTML administrativo, sem chamada extra ao abrir a página.

`AutomaticRecoveryPreservesLegacyHistoryRollsBackOutcomeFailureAndRacesSafely` usa PostgreSQL efêmero no CI: aplica upgrade com execução legada, preserva resultado desconhecido, força falha na gravação do resultado automático e exige rollback financeiro mantendo intenção, verifica intervalo no SQL, disputa oito workers em conexões distintas, exige uma captura e duas execuções automáticas incluindo a interrompida, recusa resultado ausente/downgrade destrutivo e lê o resumo administrativo. Não usa credenciais nem banco operacional.

`PaymentRecoveryExecutionTests` verifica identidade, UTC, limite exato, transições terminais e versão. `PaymentRecoveryTests` cobre outra solicitação durante GET sem nova chamada, cancelamento com execução aberta, nova ação após expiração, resposta expirada sem substituição e resposta antiga chegando depois de uma substituição concluída: nenhuma pode confirmar captura. Uma consulta posterior válida confirma uma única venda. Indisponibilidade encerrada possui execução `Completed`, sem significar aprovação.

`PaymentRecoveryWebTests` verifica formulário oculto enquanto vigente, POST bloqueado, liberação após limite e leitura sem recuperação automática. HTML é verificado por TestServer, não por homologação visual autenticada.

`RecoveryOwnershipBlocksParallelGetFencesOldResponseAndPreservesHistory` executa no PostgreSQL efêmero do CI: oito contextos concorrentes não iniciam outro GET; índice único e constraint de encerramento recusam registros inválidos; substituição impede efeito da resposta antiga; query administrativa lê o histórico e downgrade com dados é recusado. O teste de rollback de auditoria também exige execução ainda aberta após falha, bloqueio antes do limite e disputa com webhook após expiração. Provedor continua simulado; banco real não equivale a homologação no Mercado Pago.

## Consulta administrativa de pagamentos — 28/09/2026

Incremento de 29/09: `PaymentRecoveryFollowUpTests` verifica motivos isolados/combinados, limite exato de 24 horas, orçamento sem contar consultas manuais, ausência de mutation, flags desligadas, consulta vigente até o instante de expiração, exclusão de captura/revisão/ID ausente/produção e paginação de 31 tentativas com pedidos cancelados. HTML mantém `no-store`, `noindex`, filtro no cursor e orientação sem expor PII. Autorização recusa anônimos e Customer no novo filtro. O fluxo HTTP habilitado verifica que conferência manual ainda exige POST confirmado e que captura tardia vai para revisão sem movimento de estoque.

`FollowUpQueueUsesDurableLimitsAndLeaseBoundariesWithoutChangingPayment` exercita no PostgreSQL efêmero a consulta de filtro/motivos, cursor, limite temporal, orçamento, bloqueio por execução vigente e detalhe de pedido cancelado. As consultas não alteram versões, execuções, auditoria ou estoque. Não equivale a homologação com o provedor.

`AdminPaymentQueryTests` cobre revisão ligada a pedido cancelado, envio sem ID, captura preservada após revisão, quatro filtros, paginação sem duplicatas com timestamps iguais, limite de 50 recibos, parâmetros inválidos, detalhe inexistente e ausência de mutation. O pipeline HTTP verifica conteúdo Razor, cache `no-store`, navegação, ausência de PII/tokens/chaves/hash no HTML e recusa de POST. `AdminAuthorizationTests` recusa anônimos e clientes tanto na lista quanto no detalhe. A consulta usa apenas banco, sem chamadas ao gateway.

Os testes PostgreSQL existentes também exercitam as projeções da consulta, filtros/cursor e leitura de recibos capturados no banco efêmero do CI. Validação de HTML por TestServer não substitui homologação visual no navegador com uma conta administrativa autorizada; não criar ou publicar credenciais de demonstração para acessar a área.

## Recuperação interna de pagamentos — 28/09/2026

`PaymentRecoveryWebTests` cobre o fluxo HTTP Admin → formulário com antiforgery/versão → GET simulado do provedor → confirmação/auditoria → redirect/detalhe, sem segunda venda após repetição. Verifica confirmação/motivo/versão obrigatórios, antiforgery inválido, ação desabilitada, anônimo/Customer, role revogada, limite de cinco requisições por minuto, GET sem efeitos e tentativa ausente. `AdminPaymentQueryTests` verifica limite/ordenação do histórico e ausência de JSON arbitrário/PII no HTML; a consulta também é executada no PostgreSQL efêmero. A homologação visual com conta autorizada e a integração real permanecem pendentes.

`PaymentRecoveryTests` cobre captura recuperada sem recibo falso, auditoria antes/depois do GET, repetição sem segunda venda, webhook chegando durante/depois da consulta, Admin revogado durante HTTP, pedido alterado, aprovação tardia/cancelada, 404/indisponibilidade, resposta inválida, observação sem captura, ausência de ID, revisão não liberada, contexto dirty preservado e cancelamento com intenção aberta. Os gateways de teste recusam métodos de criação: recuperação só pode consultar.

`RecoveryAuditFailureRollsBackCaptureAndRecoveryCompetesSafelyWithWebhook` usa PostgreSQL efêmero no CI: provoca falha na auditoria de conclusão e exige rollback dos efeitos financeiros, preservando apenas a intenção; depois disputa recuperação e webhook em contextos/conexões independentes e exige exatamente uma venda e uma evidência de confirmação. O teste não usa credenciais reais e só remove seu próprio banco temporário.

## Camadas

### Unit tests

Usar xUnit para domínio e aplicação sem rede/banco:

- cálculo de item, subtotal, desconto, frete e total;
- elegibilidade e limites de cupom;
- máquina de estados de pedido/pagamento/envio;
- reserva, consumo e liberação conceitual de estoque;
- normalização e validações de value objects;
- mapeamento de estado externo para interno;
- regras de guest/account linking.

Fakes pequenos são preferidos para `IClock` e fronteiras externas. Não testar framework ou getter trivial.

### Integration tests

Usar `WebApplicationFactory` com banco controlado. Fluxos HTTP que não dependem de semântica específica do provedor podem usar EF Core InMemory para feedback rápido; constraints, migrations, transações e concorrência exigem PostgreSQL real efêmero via Testcontainers:

- mappings, constraints, índices e migrations;
- transações e concorrência de estoque;
- Identity, cookies, antiforgery e policies;
- controllers/endpoints e model binding;
- idempotência e deduplicação de webhook;
- persistência de snapshots;
- upload e storage temporário;
- health checks e tratamento de erro.

Mercado Pago, Melhor Envio e e-mail usam servidores HTTP fake controlados nos testes; sandbox é reservado para homologação, não para suíte determinística.

### Cobertura implementada da conta

- cadastro cria `ApplicationUser`, role e `Customer`, sem autenticar antes da confirmação;
- confirmação por token libera login e cookie seguro;
- recuperação responde igual para e-mail conhecido e desconhecido e permite trocar a senha;
- POST sem antiforgery é rejeitado;
- a sexta tentativa de cadastro na mesma janela/IP recebe `429`;
- cinco senhas incorretas bloqueiam a conta e a senha correta não ignora o lockout;
- cliente não lê nem altera endereço pertencente a outro usuário;
- sender fake captura links em memória sem expô-los em log.

### Cobertura implementada do catálogo

- normalização de slug, invariantes editoriais e rejeição de chaves de imagem com traversal;
- rascunho não aparece no catálogo público e slug/SKU duplicado é rejeitado;
- publicação exige imagem e variante comercializável;
- atualização com versão obsoleta é rejeitada por concorrência otimista;
- ajuste de estoque gera movimento e auditoria e não aceita saldo inválido;
- mudança de slug preserva redirect permanente para a URL canônica;
- catálogo vazio responde com estado editorial seguro e o Admin exige autorização.
- upload válido produz três WebP e mídia com `nosniff`/cache imutável;
- extensão disfarçada, tamanho excessivo, dimensão excessiva e traversal são rejeitados;
- troca de capa, reordenação e remoção mantêm a galeria consistente;
- conflito de upload remove os arquivos órfãos gerados antes da falha no banco.
- home exibe somente produtos publicados e explicitamente destacados;
- variante esgotada pode ser selecionada sem JavaScript e permanece identificada como indisponível;
- canonical ignora filtros/variante e Open Graph usa a imagem processada com dimensões coerentes;
- JSON-LD contém `Product`, ofertas e disponibilidade derivada do estoque;
- slug antigo responde 301 e produto arquivado responde 404.

### Cobertura implementada do carrinho e cupons

- token guest é armazenado somente como hash e adulteração não cria nem lê carrinho;
- preço, estoque e desconto são recalculados pelo servidor após cada alteração relevante;
- quantidade, variante inativa, remoção, limpeza, expiração e mesclagem após login são exercitadas;
- código, valor, janela, mínimo e limites do cupom possuem testes de invariantes;
- percentual e valor fixo são arredondados e rateados de forma determinística sem total negativo;
- aplicação aceita um único cupom, reflete mudança de preço e marca mínimo não atendido sem conceder desconto;
- reserva é idempotente, consumo não duplica efeito e cancelamento/expiração libera o limite;
- limite por e-mail normalizado e corrida pelo último uso permitem somente a reserva elegível;
- rotas de cupom exigem antiforgery e `/Admin/Cupons` exige role `Admin`;
- criação e atualização administrativa registram auditoria sem permitir exclusão histórica.

### Cobertura implementada da entrada do checkout

- guest revisa contato e entrega sem criação de conta;
- cliente recebe perfil e endereços ativos como pré-preenchimento, sem atualização implícita;
- ID de endereço pertencente a outra conta é rejeitado antes de usar seus dados;
- nome, e-mail, telefone, CEP, espaços e UF são normalizados novamente no servidor;
- campos ausentes ou malformados voltam associados ao campo correto;
- CPF, aceite genérico, preço e total não integram o modelo de entrada;
- campos extras por overposting são ignorados e o resumo continua derivado da sacola;
- carrinho vazio ou indisponível não abre checkout, e POST sem antiforgery recebe `400`.

### Cobertura implementada de pedidos e ciclo de vida

- snapshots comerciais e de endereço permanecem imutáveis após alteração do catálogo;
- criação repetida pela mesma tentativa devolve o pedido existente sem duplicar estoque, cupom ou cliente guest;
- duas compras concorrentes da última unidade têm um único vencedor e falha parcial faz rollback completo;
- a matriz de estados testa todas as combinações, aceitando somente as arestas documentadas;
- expiração respeita o limite do lote e libera reserva, saldo e cupom exatamente uma vez;
- transição manual exige motivo e versão atuais, repetição é idempotente e versão obsoleta retorna conflito;
- entrada e saída de `RequiresAttention` preservam ou limpam o contexto corretamente;
- ações manuais registram ator, motivo e correlação em auditoria; expiração técnica não cria ator fictício.

### Cobertura implementada da fundação de frete

- o cliente Melhor Envio envia o payload por produtos, CEPs normalizados, valor declarado, dimensões, peso e quantidade atuais;
- base URL sandbox, timeout, `Accept` e `User-Agent` obrigatório são conferidos na configuração do cliente HTTP;
- apenas respostas válidas produzem opções; uma resposta vazia não vira frete grátis e não é armazenada no cache;
- cache atende repetição equivalente e a revalidação força nova consulta;
- CEP inválido não chama o provedor; `401/403`, `400/422`, `429`, indisponibilidade, timeout e resposta inválida são normalizados;
- o checkout remonta a cotação pelo carrinho e catálogo atuais, rejeita mudança de preço e só então cria um snapshot confiável;
- token, CEP e detalhe interno do provedor não aparecem nas mensagens devolvidas ao cliente.

A chamada real ao sandbox continua manual e pendente de `PBD-007`, credencial própria e e-mail de suporte. Os testes automatizados usam handlers HTTP determinísticos e não dependem da disponibilidade externa.

### Testes manuais e homologação

- responsividade, acessibilidade, conteúdo e experiência de marca;
- Checkout Pro com contas/cartões de teste;
- Melhor Envio sandbox e uma validação produtiva controlada antes do go-live;
- e-mails em clientes reais após domínio configurado;
- Nginx/Cloudflare, headers, TLS, cache e uploads;
- backup, restore, rollback e operação administrativa.

## Matriz crítica

| Área | Cenários mínimos |
|---|---|
| Preço | Servidor ignora valor do cliente; mudança no catálogo não altera pedido; arredondamento é determinístico. |
| Cupom | Inativo, expirado, mínimo, limite concorrente, uso duplicado, produto inelegível e cancelamento. |
| Estoque | Duas compras da última unidade; rollback parcial; expiração repetida; pagamento duplicado; ajuste abaixo do reservado. |
| Pedido | Toda transição válida; transições ausentes rejeitadas; comando repetido idempotente; snapshot imutável. |
| Pagamento | Preferência timeout/retry; retorno falso; assinatura inválida; webhook duplicado/fora de ordem; valor/moeda/referência divergente; reembolso. |
| Aprovação tardia | Reserva expirada com e sem estoque disponível; entrada em `RequiresAttention`; nenhuma venda silenciosa negativa. |
| Frete | CEP inválido, nenhuma opção, timeout, cotação expirada, preço alterado, token expirado, rastreio terminal. |
| Autorização | Guest, Customer e Admin; IDOR em pedido/endereço; Admin sem role; antiforgery ausente. |
| Conta | Confirmação, lockout, reset sem enumeração, vínculo só após e-mail confirmado, carrinho mesclado. |
| Upload | Extensão falsa, magic bytes inválidos, arquivo enorme, dimensão bomba, SVG/script, traversal, órfão e acesso não autorizado. |
| LGPD/logs | Ausência de segredo/PII proibida; anonimização preserva histórico necessário; cookie opcional não carrega sem base. |
| SEO | canonical, robots por ambiente, sitemap só com ativos, JSON-LD coerente, 404/redirect de slug. |

## Concorrência de estoque

O teste integrado inicia duas transações concorrentes tentando reservar a última unidade. Apenas uma confirma; a outra recebe indisponibilidade. Ao final, `OnHand >= Reserved >= 0`, existe uma reserva ativa e nenhum pedido parcial. O mesmo padrão cobre limite final de cupom.

A criação de pedido também cobre repetição da mesma tentativa, snapshots que permanecem iguais após alteração do catálogo, rateio determinístico do cupom, limpeza da sacola somente no sucesso e rollback quando uma linha posterior fica indisponível. O contrato rejeita frete zerado sem regra aprovada, cotação vencida e qualquer total recebido do navegador.

## Frete com embalagem pendente

Cobertura implementada para avançar sem estimar a caixa consolidada:

- uma unidade usa peso bruto e dimensões atuais da variante, sem adicionar novamente a tara; payloads testados para 130 g e 300 g com as dimensões comerciais confirmadas;
- duas unidades iguais ou produtos distintos retornam `PackagingRequired`, sem HTTP, opções de frete ou snapshot confirmado;
- a sacola mantém itens, quantidades e total após recusa da cotação;
- uma cotação anterior é rejeitada se a sacola passar a ter várias unidades; ao voltar a uma unidade, nova revalidação pode consultar o provedor;
- preparo médio de dois dias úteis é apresentado separadamente, sem alterar o intervalo de transporte do provedor;
- configuração de preparo aceita 0–30 dias e rejeita valores fora desse intervalo;
- tentativa de enviar prazo de preparo ou total pelo formulário não altera os valores do servidor;
- a revisão com várias unidades exibe a pendência de embalagem sem apresentar total com entrega ou frete grátis.

Os testes usam HTTP fake; não representam homologação da conta Melhor Envio, de transportadoras ou de embalagem real.

## Webhook

O endpoint Sandbox está implementado e desabilitado. `PaymentWebhookTests` cobre assinatura incorreta, troca de ID/request ID, timestamp antigo/futuro em segundos/milissegundos, duplicatas de campos, JSON inválido, corpo divergente, dados não assinados sem autoridade, dez entregas concorrentes, replay, resposta transitória e aprovação tardia. O fluxo HTTPS em TestServer usa controller/serviço reais com gateway simulado e verifica uma captura e uma venda. O endpoint tem testes de antiforgery dispensado, HTTPS obrigatório e limite de corpo. Testes de logs completos e homologação do provedor ainda são pendências.

`PostgreSqlPaymentTests.WebhookRollsBackOnFailureAndConcurrentDeliveriesConfirmExactlyOnce` aplica migrations em banco descartável, injeta falha SQL ao inserir venda e verifica rollback de captura/pedido/recibo; em seguida disputa dez conexões, verifica efeito único, constraints e bloqueio do downgrade destrutivo. São três testes PostgreSQL obrigatórios no CI e explicitamente ignorados sem banco local. Nenhuma cobrança real é feita.

Os critérios completos de homologação permanecem:

- payload com assinatura correta é consultado na API fake;
- payload sozinho nunca confirma pedido;
- dez entregas concorrentes do mesmo evento produzem um `WebhookEvent`, um movimento de estoque e uma transição;
- evento aprovado seguido de pendente não regride estado;
- evento desconhecido/valor divergente não altera pedido e gera revisão;
- resposta e logs não contêm segredo.

## Fundação local de pagamento — cobertura implementada

- snapshot de total, moeda, número do pedido, ambiente, chave e expiração;
- recusa de pedido pago, cancelado, em revisão ou expirado, ambiente inválido, chave vazia e timestamps inválidos;
- preferência repetida não altera estado/versão e uma preferência diferente não substitui a anterior;
- preferência recebida após a expiração ou após resultado incerto permanece em revisão;
- IDs inválidos/longos são recusados sem mutação; motivos são códigos fechados;
- persistência/releitura e rejeição de atualização com versão obsoleta usando EF InMemory;
- verificação de precisão, FK restritiva, índices únicos/parciais e constraints no modelo/SQL Npgsql;
- modelo EF sem alterações pendentes em relação à migration.

O teste InMemory não executa índices únicos ou constraints relacionais. `PostgreSqlPaymentTests` cobre essa parte no CI: aplica migrations em banco exclusivo com sufixo aleatório e valida oito preparações concorrentes em contextos/conexões distintos, uma única tentativa, colisão de chave/preferência/pedido, rejeição de valor negativo e atualização com versão obsoleta. Nenhum HTTP Mercado Pago é executado. Quedas entre persistência/HTTP, disputas com cancelamento e fluxo financeiro ponta a ponta ainda precisam de homologação.

### Preparação e PostgreSQL no CI

Os testes locais cobrem preparação/replay, snapshots imutáveis, estoque preservado, guest alheio ou expirado, conta vinculada, pedido inexistente/pago/cancelado/em revisão, reservas ausentes/liberadas/divergentes, resultado incerto e ambiente diferente. Verificam também recarga de pedido já rastreado e preservação de mudanças não salvas no contexto.

O job CI inicia `postgres:18.6-alpine3.24`, igual à versão do Compose, com credenciais descartáveis e `SALLVAT_TEST_POSTGRES`. O teste cria e remove somente um banco `sallvat_payment_tests_<uuid>`, sem usar o nome de banco fornecido na conexão. Não há volume persistente ou conexão à VPS. Para executar localmente, use exclusivamente uma instância de testes com permissão de criar bancos e forneça essa variável; sem ela, o teste aparece explicitamente como ignorado, nunca como aprovação relacional. O semáforo InMemory não substitui esse teste real.

## Adapter Checkout Pro — cobertura implementada

- payload com itens líquidos, frete, BRL, referência, chave estável e validade; sem dados pessoais, cartão ou política comercial inventada;
- validação do total exato, centavos, itens, prazo, ambiente e configuração antes de qualquer HTTP;
- vendedor/referência divergentes, preferência inválida e resposta após validade não geram redirect;
- HTTPS, host brasileiro exato, porta padrão, ausência de usuário/fragmento e `pref_id` correspondente;
- recusa de HTTP, domínio semelhante/malicioso, credenciais na URL e IDs divergentes;
- normalização de 400/401/403/408/409/422/429/5xx e status inesperado, sem propagar payload;
- timeout real de dois segundos, transporte interrompido, JSON malformado, formato inesperado e resposta acima de 64 KiB;
- cancelamento pré-envio sem HTTP e cancelamento pós-envio tratado como incerto;
- no máximo um envio por chamada, sem retry automático, e DI desabilitada por padrão.

Todos esses testes usam `HttpMessageHandler` fake. Não validam a conta Mercado Pago, antifraude, cobrança, webhook, conciliação, páginas de retorno ou persistência da tentativa pelo orquestrador, que seguem pendentes. O limite de ambiente inclui confirmação operacional de vendedor de teste; o prefixo do token não é uma garantia técnica de sandbox.

## Contrato Orders isolado

### Envio persistido

`PaymentDispatchTests` cobre envio concorrente e replay sem segundo POST, falhas persistidas, cancelamento/expiração durante HTTP, preservação do ID tardio sem URL, cancelamento do navegador independente da gravação final, claim abandonado, falha de banco após HTTP, titularidade, flag desligada e rateio exato de centavos. O domínio impede troca de proprietário do claim e mistura de Orders com Preferences.

`PostgreSqlPaymentTests` aplica a nova migration no banco isolado e executa oito dispatchers em contextos independentes com gateway simulado, verificando um único envio, unicidade do ID externo, constraints do claim e recusa de rollback que apagaria registros de envio. Sem PostgreSQL local, esses dois testes relacionais são explicitamente ignorados; ambos são obrigatórios no CI. Não há comprovação de webhook ou conciliação externa ainda.

### Adapter HTTP

`MercadoPagoOrderGatewayTests` usa somente HTTP simulado. Cobre payload separado de Preferences; valores invariantes sob cultura `pt-BR`; exemplo de desconto de um centavo distribuído em linhas distintas e frete explícito; chave estável; URLs de retorno; flags independentes/exclusivas; bloqueio de produção; soma inválida, duplicidade de linhas, expiração e entrada inválida sem HTTP; vendedor/referência/moeda/país/valor/status divergentes; URLs maliciosas; JSON inválido e limite de resposta; timeout/cancelamento/transporte; ausência de retry, fallback e exposição de `client_token`. Não comprova homologação do provedor, envio persistido ou webhook.

## Consulta Orders e diagnóstico de conciliação

`MercadoPagoOrderQueryTests` verifica GET em host fixo, Bearer sem chave de criação, corpo ausente, ID/vendedor/referência/país/moeda/valor divergentes, timestamps explícitos (offset e nanossegundos), propriedades duplicadas, estado desconhecido, transações e ausência de dados pessoais/tokens no resultado. Testa 401/403/404/302/429/500, resposta malformada/grande, transporte, timeout e cancelamento. Configuração desligada e produção não fazem HTTP; consulta nunca executa POST ou retry.

`PaymentReconciliationTests` verifica snapshots persistidos, replay somente leitura, dono alheio, registro inexistente, contexto alterado, claim sem ID (incerto ou abandonado), erros do gateway, atividade financeira, pedido expirado e resposta tardia. Cancelamento durante HTTP é detectado por releitura de versão; nenhuma consulta altera tentativa, pedido ou estoque. São testes simulados, não homologação financeira. Diagnóstico não implementa confirmação transacional, recuperação de ID ou webhook.

## Segurança automatizada

- análise de dependências e imagem no pipeline;
- testes de headers, cookies e HTTPS atrás do proxy;
- requests sem antiforgery e com overposting;
- rate limiting e lockout sob relógio controlado;
- autorização por recurso, não apenas ocultação de link;
- validação de tamanho antes de alocação de upload.

## Fluxo de homologação

1. publicar artefato imutável em Staging;
2. resetar dados sintéticos e executar smoke automatizado;
3. cadastrar produto/variante/imagem/cupom pelo Admin;
4. comprar como guest e autenticado;
5. validar cotação, pagamento aprovado/rejeitado/pendente e duplicata;
6. preparar, gerar envio, rastrear e reembolsar;
7. revisar logs/auditoria e ausência de dados sensíveis;
8. validar SEO, acessibilidade e responsividade;
9. testar backup/restore quando houver mudança operacional;
10. registrar evidências, falhas e aprovação.

## Definição de pronto de uma story

- critérios de aceite implementados;
- testes proporcionais ao risco passam localmente e no CI;
- logs, segurança e auditoria foram considerados;
- migrations e integração têm caminho de falha testado;
- documentação/ADR atualizados quando comportamento ou decisão mudou;
- sem segredo, dado real ou dependência não aprovada no repositório.
