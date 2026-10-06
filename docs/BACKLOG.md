<!-- markdownlint-disable MD013 MD024 MD060 -->

# Backlog executável

Este backlog segue o [ROADMAP.md](ROADMAP.md). `EPIC` corresponde a uma fase/resultado, `STORY` descreve valor verificável e `TASK` é unidade de execução. Itens da Fase 0 já materializados estão marcados; aprovação comercial permanece aberta.

Revisão em 01/10/2026: `[x]` indica entrega técnica verificável, não homologação comercial/produtiva. [STATUS.md](STATUS.md) consolida fases, limitações e prioridades. Tarefas compostas foram separadas quando implementação e validação externa tinham estados diferentes.

## EPIC F0 — Descoberta e documentação

### STORY F0-S1 — Equipe possui uma fonte de verdade técnica

**Aceite:** todos os documentos existem, são coerentes e navegáveis.

#### TASK

- [x] Documentar visão, MVP, pós-MVP e home.
- [x] Numerar requisitos e decisões comerciais pendentes.
- [x] Definir arquitetura, módulos, dependências e contratos.
- [x] Modelar banco, estoque e snapshots em ERD.
- [x] Definir pedidos, pagamento, frete e autenticação.
- [x] Definir segurança, LGPD, observabilidade, infraestrutura e deploy.
- [x] Criar roadmap, backlog, testes, SEO e ADRs.
- [x] Revisar documentação contra código, flags, migrations e workflows; consolidar estágio, pendências e guias de desenvolvimento/configuração/operação em 01/10/2026.
- [ ] Revisar e aprovar a documentação com a Sallvat.
- [ ] Atribuir responsável e prazo às PBDs que bloqueiam as próximas fases.

## EPIC F1 — Fundação técnica

### STORY F1-S1 — Desenvolvedor executa a aplicação localmente

**Aceite:** clone limpo compila, testa e inicia web/PostgreSQL seguindo o README.

#### TASK

- [x] Criar `Sallvat.sln` e projetos `Web`, `Application`, `Domain`, `Infrastructure`.
- [x] Criar projetos `UnitTests` e `IntegrationTests`.
- [x] Configurar referências permitidas e teste de arquitetura.
- [x] Habilitar nullable, warnings relevantes, analyzers e formatação.
- [x] Criar `global.json`, lock files e documentação de pré-requisitos.
- [x] Configurar PostgreSQL Development via Compose sem exposição externa produtiva.
- [x] Criar `SallvatDbContext` inicial e registrá-lo no composition root.
- [x] Criar a primeira migration somente quando existir o primeiro schema real.
- [x] Documentar comandos de build, test e migration.

### STORY F1-S2 — Aplicação possui base operacional segura

**Aceite:** erro, logs, correlação, health e configuração funcionam por ambiente.

#### TASK

- [x] Configurar options tipadas e validação no startup.
- [x] Configurar Serilog JSON e filtros de dados sensíveis.
- [x] Implementar/generar correlation ID e propagação HTTP.
- [x] Implementar tratamento global de exceções e páginas seguras.
- [x] Criar `/health/live` e `/health/ready`.
- [x] Persistir Data Protection keys fora do container efêmero.
- [x] Configurar Tailwind, purge/content paths e build de assets.
- [x] Criar layout Razor mínimo acessível e sem design final.
- [x] Criar pipeline CI de restore, build e testes.

## EPIC F2 — Identidade e clientes

### STORY F2-S1 — Visitante cria e recupera uma conta com segurança

**Aceite:** cadastro, confirmação, login, logout e reset passam por e-mail e controles antiabuso.

#### TASK

- [x] Criar `ApplicationUser` com chave `Guid` e configurações Identity.
- [x] Criar migration Identity e constraints de e-mail.
- [x] Implementar cadastro com view model allowlisted.
- [x] Implementar confirmação de e-mail e expiração de token.
- [x] Implementar login, logout POST e lockout.
- [x] Implementar solicitação/reset de senha sem enumeração.
- [x] Implementar alteração de senha e invalidação de sessão.
- [ ] Integrar `IEmailSender` ao provedor definido em `PBD-010`.
- [x] Testar cookies, antiforgery, rate limits e fluxos de erro.

### STORY F2-S2 — Cliente gerencia perfil, endereços e histórico vinculado

**Aceite:** apenas o titular confirmado acessa e altera seus dados.

#### TASK

- [x] Criar entidades/configurações `Customer` e `Address`.
- [x] Criar migration, índices e constraints.
- [x] Implementar criação/associação de perfil durante cadastro.
- [ ] Implementar criação/associação de perfil durante guest checkout.
- [x] Implementar CRUD de endereços com autorização por recurso.
- [x] Criar páginas de conta e pedidos ainda vazias/contratadas.
- [ ] Implementar vínculo de pedidos guest após confirmação do e-mail.
- [ ] Tratar colisão com pedido já vinculado sem transferência automática.
- [x] Testar IDOR de endereços.
- [ ] Testar alteração de e-mail e vínculo guest.

### STORY F2-S3 — Operação possui acesso administrativo individual

**Aceite:** `/Admin` rejeita não administradores e cada ação identifica o ator.

#### TASK

- [x] Criar roles `Customer` e `Admin` de forma idempotente.
- [x] Criar Area `/Admin` e policy de acesso.
- [ ] Implementar procedimento seguro para conta Admin inicial.
- [ ] Exigir troca de segredo inicial/revisar e-mail confirmado.
- [ ] Documentar concessão e revogação conforme `PBD-016`.
- [x] Testar acesso anônimo, Customer e Admin.

## EPIC F3 — Catálogo, imagens e estoque

### STORY F3-S1 — Administrador cadastra e publica perfume com variantes

**Aceite:** produto publicado aparece por slug com variante vendável e dados físicos válidos.

#### TASK

- [x] Criar entidades `Product` e `ProductVariant` e invariantes.
- [x] Configurar mappings, precisão, índices únicos, checks e concorrência.
- [x] Criar migration e dados de teste não produtivos.
- [x] Implementar casos de uso de criar, editar, publicar, inativar e destacar.
- [x] Criar controllers/views Admin com validação e antiforgery.
- [x] Impedir publicação sem preço, SKU, peso, dimensões e imagem necessários.
- [x] Auditar preço, status e alteração comercial.
- [x] Testar slug/SKU duplicado, overposting e conflito de edição.

### STORY F3-S2 — Administrador gerencia imagens seguras e otimizadas

**Aceite:** somente imagens válidas geram WebP/thumbnails fora do web root.

#### TASK

- [x] Criar `ProductImage` e configuração.
- [x] Definir e implementar `IImageStorage` local.
- [x] Selecionar biblioteca de imagem após revisão de licença e segurança.
- [x] Validar tamanho, extensão, magic bytes, dimensões e decodificação.
- [x] Remover metadados e gerar variantes WebP.
- [x] Implementar upload, ordenação, capa e remoção compensável.
- [x] Configurar entrega/cache/headers no ambiente local.
- [x] Testar arquivo disfarçado, bomba de dimensão, traversal, órfão e concorrência.

### STORY F3-S3 — Estoque é ajustado com histórico e concorrência

**Aceite:** saldo nunca fica abaixo do reservado e todo ajuste tem motivo/ator.

#### TASK

- [x] Criar `InventoryMovement` e campos `OnHand`, `Reserved`, versão.
- [x] Implementar ajuste condicional de estoque.
- [x] Criar tela Admin de saldo e movimentos.
- [x] Exigir justificativa em ajuste manual.
- [x] Auditar antes/depois e movimento resultante.
- [x] Testar conflito, quantidade inválida e redução abaixo do reservado.

### STORY F3-S4 — Visitante descobre produtos indexáveis

**Aceite:** catálogo e produto funcionam sem JS e só expõem conteúdo publicado.

#### TASK

- [x] Implementar query paginada de catálogo e filtros essenciais.
- [x] Implementar página `/perfumes/{slug}` com seleção de variante.
- [x] Implementar home e destaques com conteúdo demonstrativo.
- [ ] Aprovar conteúdo definitivo, preços, notas e imagens antes de cadastrar/publicar catálogo produtivo (PBD-002).
- [x] Gerar title, description, canonical, Open Graph e JSON-LD.
- [x] Implementar 404 e histórico/redirect 301 de slug.
- [x] Aplicar imagens responsivas e acessibilidade básica.
- [x] Testar produto inativo, sem estoque, slug antigo e metadados.

## EPIC F4 — Carrinho e cupons

### STORY F4-S1 — Visitante mantém carrinho confiável

**Aceite:** itens persistem sem confiar em preço ou estoque do navegador.

#### TASK

- [x] Criar `Cart` e `CartItem`, mappings, índices e migration.
- [x] Gerar token guest aleatório e cookie seguro.
- [x] Implementar adicionar, alterar quantidade, remover e limpar.
- [x] Recalcular catálogo, preço e disponibilidade em cada resumo relevante.
- [x] Implementar carrinho autenticado e mesclagem após login.
- [x] Implementar expiração e limpeza em lote.
- [x] Criar views responsivas e mensagens de alteração de preço/estoque.
- [x] Testar cookie adulterado, variante inativa, limite de quantidade e mesclagem.

### STORY F4-S2 — Administrador cria cupom com limites verificáveis

**Aceite:** cupom válido aplica desconto determinístico e não ultrapassa limite concorrente.

#### TASK

- [x] Criar `Coupon` e `CouponRedemption` com invariantes.
- [x] Criar mappings, índices, checks e migration.
- [x] Implementar cálculo central de desconto e rateio.
- [x] Implementar CRUD Admin, ativação e auditoria.
- [x] Aplicar padrão não acumulável enquanto `PBD-009` estiver pendente.
- [x] Implementar reserva/consumo/liberação de limite junto ao pedido.
- [x] Testar expiração, mínimo, uso por cliente/e-mail, corrida e cancelamento.

## EPIC F5 — Checkout e pedidos

### STORY F5-S1 — Guest ou cliente informa comprador e entrega

**Aceite:** checkout coleta apenas dados necessários e valida tudo no servidor.

#### TASK

- [x] Criar view models em etapas ou formulário único conforme teste de UX.
- [x] Implementar validação e normalização de contato/CEP/endereço.
- [x] Pré-preencher dados autenticados sem exigir salvamento.
- [x] Não coletar CPF enquanto `PBD-003` não exigir.
- [x] Evitar coleta de CPF e aceite genérico enquanto políticas estão pendentes; confirmar explicitamente a operação de checkout de teste.
- [ ] Publicar políticas aprovadas e integrar confirmação/informação aplicável ao checkout, sem checkbox abusivo (PBD-006/PBD-012).
- [x] Testar overposting, campos ausentes, endereço de outro cliente e guest.

### STORY F5-S2 — Sistema cria pedido e reserva estoque atomicamente

**Aceite:** pedido completo ou nenhum efeito; concorrência não causa overselling.

#### TASK

- [x] Criar `Order`, `OrderItem`, `OrderAddress` e estados.
- [x] Criar `StockReservation` e configurações/migration.
- [x] Implementar calculador único de totais.
- [x] Implementar snapshots de item, contato, endereço, cupom e frete.
- [x] Implementar update condicional de reserva em ordem estável.
- [x] Gerar número público único e expiração configurável.
- [x] Tornar criação idempotente por tentativa de checkout.
- [x] Limpar/associar carrinho somente após sucesso.
- [x] Testar rollback, última unidade concorrente, total adulterado e snapshot.

### STORY F5-S3 — Pedidos expiram e transitam de forma controlada

**Aceite:** apenas transições documentadas ocorrem e repetição não duplica efeito.

#### TASK

- [x] Implementar máquina de estados no domínio.
- [x] Implementar job de expiração em lotes.
- [x] Liberar reserva/cupom idempotentemente.
- [x] Criar casos de uso Admin com versão e justificativa.
- [x] Criar `RequiresAttention` e fila mínima de sinalização; resolução guiada permanece em F8-S1.
- [x] Auditar transições manuais.
- [x] Testar todas as arestas válidas e inválidas.

## EPIC F6 — Frete e Melhor Envio

**Atualização comercial — 21/09/2026:** CEP, preparo de dois dias úteis e embalagens individuais confirmados em [SHIPPING.md](SHIPPING.md#dados-comerciais-confirmados-em-21092026). Pedidos com vários itens usam caixa maior, cujas medidas/peso/capacidade ainda precisam ser informados. Cadastro no Melhor Envio existente, integração ainda não autorizada/configurada. Estas confirmações não concluem as tarefas técnicas abaixo.

### STORY F6-S1 — Cliente recebe cotações válidas por CEP

**Aceite:** opções refletem itens físicos e falha nunca resulta em frete gratuito acidental.

#### TASK

- [x] Definir DTOs internos e `IFreightService`.
- [x] Implementar cliente Melhor Envio com options, timeout e `User-Agent`.
- [ ] Implementar autenticação/refresh conforme credencial aprovada.
- [ ] Implementar algoritmo de embalagem validado com `PBD-007`.
- [x] Implementar cotação e cache curto sem PII excessiva.
- [x] Revalidar opção no checkout e tratar mudança de preço.
- [x] Configurar origem/contato confirmados, apresentar preparo médio de 2 dias úteis separado do transporte e validar limites da configuração.
- [x] Bloquear cotação/revalidação automática de múltiplas unidades sem caixa consolidada validada, preservando a sacola e impedindo fallback para frete zero.
- [x] Persistir snapshot de opção, preço e prazo de transporte no pedido.
- [ ] Persistir prazo de preparo separado e integrar futuro snapshot de shipment.
- [x] Testar CEP, nenhuma cotação, timeout e 401/429 com HTTP simulado.
- [ ] Homologar cotação no sandbox real com embalagem/serviços aprovados.

### STORY F6-S2 — Operação prepara integração de envio e rastreio

**Aceite:** criação repetida não compra duas etiquetas e estados logísticos são próprios.

#### TASK

- [ ] Criar `Shipment` e `ShipmentStatus`.
- [ ] Implementar criação/consulta/cancelamento de envio quando suportado.
- [ ] Implementar armazenamento protegido da etiqueta.
- [ ] Implementar tracking query e mapeamento de estados.
- [ ] Criar fakes HTTP determinísticos e testes de idempotência.
- [ ] Documentar limitações produtivas e processo de postagem.

## EPIC F7 — Mercado Pago

### STORY F7-S1 — Cliente abre checkout Orders com envio exclusivo

**Aceite:** pedido local existe antes do gateway e timeout não cria cobrança duplicada.

#### TASK

- [x] Definir `IPaymentGateway` e resultados internos para criar preferência (consulta/reembolso seguem em F7-S2/F7-S3).
- [x] Criar a fundação de `Payment`, mappings, índices e migration, sem chamada externa.
- [x] Testar snapshots, preferência repetida/tardia, resultado incerto e token de concorrência da tentativa.
- [x] Automatizar migration, unicidade e disputa entre contextos/conexões em PostgreSQL efêmero no CI.
- [ ] Homologar fluxo financeiro completo, quedas entre persistência/HTTP e disputa com cancelamento em Staging antes de ativar cobranças.
- [x] Implementar cliente Checkout Pro isolado com options, desabilitado por padrão e limitado a Sandbox.
- [x] Montar criação de preferência com referência, valores, chave estável, validade e URLs HTTPS, testada com HTTP fake.
- [ ] Homologar conta de teste e posteriormente habilitar configuração de produção com validação de ambiente.
- [x] Revisar Preferences versus Orders API e registrar a preparação independente no ADR-015.
- [x] Implementar contrato de criação Orders isolado, com tipos separados, flags exclusivas, validação e HTTP simulado, sem fallback entre APIs.
- [x] Persistir ID externo Orders e posse exclusiva do envio, revalidar reservas antes do HTTP e bloquear reenvio ambíguo.
- [x] Montar linhas Orders a partir dos snapshots, conservando centavos do desconto e incluindo frete uma vez.
- [x] Implementar consulta/reconciliação com ID conhecido e notificações; envios sem ID permanecem pendentes em F7-S3.
- [x] Persistir uma tentativa Sandbox por pedido com autorização, snapshots, reservas e concorrência, sem acionar gateway.
- [x] Conectar revisão e pedido local ao dispatcher Orders Sandbox com duas confirmações, proteção Data Protection por dono, revalidação de valores/frete e falhas sanitizadas; desativado por padrão, sem fallback para Preferences.
- [x] Bloquear retry de POST após posse durável e consultar resultado incerto quando houver ID conhecido.
- [x] Criar páginas de retorno não autoritativas, autorizadas por conta/sessão guest, sem gateway ou efeitos financeiros em GET.
- [x] Testar fluxo MVC simulado, replay, token expirado/adulterado/alheio, antiforgery, mudança de preço/cupom/frete, limites e retorno falso; acrescentar criação concorrente revisada em PostgreSQL isolado.
- [ ] Homologar revisão, duas confirmações, retorno e perda de sessão no servidor Staging com contas de teste; aprovar políticas comerciais antes de qualquer compra real.
- [x] Testar payload, ambiente, timeout, cancelamento, ausência de retry automático e URL no adapter isolado.
- [x] Testar ausência de re-POST e recuperação persistida com fakes/PostgreSQL isolado.
- [ ] Homologar interrupções do fluxo financeiro ponta a ponta em Staging antes de habilitar compras.

### STORY F7-S2 — Webhook confirma pagamento uma única vez

**Aceite:** assinatura + consulta canônica confirmam exatamente um pagamento/estoque.

#### TASK

- [x] Criar `WebhookEvent`, unique constraints e migration.
- [x] Implementar endpoint com limite de corpo e sem antiforgery.
- [x] Validar `x-signature` e segredo no ambiente Sandbox; produção permanece recusada.
- [x] Deduplicar entrega assinada antes de efeitos, sem confiar no ID não assinado do corpo.
- [x] Consultar Orders e validar referência, valor, moeda e ambiente com snapshots locais.
- [x] Implementar GET canônico Orders com validação de identidade/snapshots, resposta sanitizada, limites e HTTP fake.
- [x] Conectar a consulta ao webhook e validar captura integral de transação única; outros cenários exigem revisão.
- [x] Aplicar `PaymentStatus`, `OrderStatus` e estoque na mesma transação.
- [x] Responder corretamente a duplicata, falha transitória e payload inválido em testes simulados.
- [ ] Homologar assinatura, timestamps/reenvios, proxy HTTPS e captura real em conta de teste antes de ativar.
- [x] Testar concorrência e ordem de eventos com fakes/PostgreSQL isolado.
- [ ] Validar logs reais do proxy/aplicação/provedor sem segredos durante homologação.

### STORY F7-S3 — Operação concilia e reembolsa pagamento

**Aceite:** divergência é visível e reembolso só altera pedido após confirmação.

#### TASK

- [x] Implementar job limitado de recuperação de pendentes/notificações perdidas com ID conhecido, orçamento persistido, intervalos progressivos e origem/resultado do sistema; desativado por padrão.
- [x] Destacar tentativas com orçamento automático esgotado ou janela encerrada na fila Admin, com motivos, paginação e orientação de conferência manual, sem alterar estado financeiro.
- [ ] Homologar job com provedor real de teste, avaliar janela/limites e operação de pendências após esgotamento; não habilitar produção nesta etapa.
- [x] Persistir controle de execução da recuperação por tentativa, janela técnica, bloqueio concorrente, interrupção auditada e rejeição de resposta antiga; manter retomada manual e integração desabilitada.
- [x] Implementar caso de uso interno de recuperação com ID conhecido, Admin atual, versão, GET canônico e auditoria atômica compartilhada com webhook; desabilitado por padrão e sem endpoint.
- [x] Conectar recuperação à interface administrativa com antiforgery, limite de requisições e histórico de resultados, sem permitir reenvio de cobrança; desabilitada por padrão.
- [x] Implementar diagnóstico interno somente leitura de tentativas com ID, titularidade e releitura após HTTP, sem efeitos financeiros.
- [ ] Recuperar claims sem ID com evidência de associação e auditoria, sem busca por referência seguida de vínculo automático.
- [x] Preparar intenção local de reembolso total Sandbox com Admin, motivo, confirmação, versões, auditoria transacional e unicidade por captura; exibir fila sem envio externo, desabilitada por padrão.
- [x] Implementar envio total Sandbox a partir da intenção, com consulta prévia, claim durável auditado, chave estável e nenhum re-POST após início; resultado incerto permite somente consulta, desativado por padrão.
- [x] Implementar confirmação total por webhook/consulta Admin com captura/valor/versões, evidência atômica e transição de pedido/pagamento, sem reposição automática de estoque/cupom.
- [ ] Homologar envio/consulta de reembolso em conta real de teste, payloads, notificações atrasadas e operação de claims que podem não ter chegado ao provedor; não habilitar produção automaticamente.
- [x] Encaminhar aprovação tardia/divergência para `RequiresAttention`, sem nova reserva automática.
- [ ] Implementar resolução auditada de aprovação tardia conforme PBD-005, sem transição silenciosa.
- [x] Criar consulta Admin local de tentativas/divergências, filtros, detalhe e recibos, sem dados pessoais ou efeitos financeiros.
- [ ] Homologar recuperação no painel e acrescentar resolução auditada de divergências, sem retry de POST financeiro.
- [x] Auditar preparação, início e conclusão/consulta humana de reembolso atomicamente; webhook mantém recibo próprio.
- [ ] Auditar a futura resolução manual de divergências financeiras.
- [x] Testar falha/timeout sem re-POST, valor divergente e aprovação tardia sem recompor estoque, com provedores simulados.
- [ ] Homologar esses cenários com contas reais de teste e responsáveis operacionais.

## EPIC F8 — Operação e comunicação

### STORY F8-S1 — Administrador conduz pedido do pago ao entregue

**Aceite:** dashboard oferece ações apenas compatíveis com o estado.

#### TASK

- [ ] Criar dashboard e filtros por estado/data/número.
- [ ] Criar detalhe com snapshots, pagamentos, shipment e auditoria permitida.
- [ ] Implementar `Paid → Preparing` com concorrência.
- [ ] Integrar compra/impressão de etiqueta sem duplicação.
- [ ] Implementar postagem, `Shipped`, rastreio e `Delivered`.
- [ ] Implementar resolução guiada de `RequiresAttention`.
- [ ] Testar autorização, concorrência e transições.

### STORY F8-S2 — Cliente recebe comunicação e acompanha pedido

**Aceite:** mensagens não duplicam e acesso guest exige link assinado.

#### TASK

- [ ] Criar templates transacionais de confirmação, pagamento, envio e entrega.
- [ ] Implementar envio/reenvio idempotente e log sanitizado.
- [ ] Implementar link guest aleatório, assinado, expirável e revogável.
- [ ] Criar histórico do cliente autenticado com autorização por recurso.
- [ ] Implementar job de rastreio com backoff.
- [ ] Garantir que falha de e-mail não reverta pedido.
- [ ] Testar link expirado/adulterado, duplicata e indisponibilidade do provedor.

## EPIC F9 — Segurança, LGPD e hardening

Gaps confirmados na revisão documental: middleware/proxy confiável, headers globais, duração menor de sessão Admin, limiter dedicado de cotação e health de storage ainda não entregues. Políticas descritas em SECURITY não são garantia de implementação.

### STORY F9-S1 — Aplicação resiste aos abusos prioritários

**Aceite:** checklist de segurança passa sem achado crítico/alto.

#### TASK

- [ ] Configurar HTTPS/HSTS e forwarded headers confiáveis.
- [ ] Aplicar CSP, headers e política de framing/referrer/permissões.
- [ ] Revisar antiforgery, encoding, sanitização e overposting.
- [ ] Aplicar rate limits e testar lockout.
- [ ] Revisar autorização por recurso e Area Admin.
- [ ] Revisar uploads, webhooks, SSRF/path traversal e limites.
- [ ] Executar análise de dependências/imagens e corrigir achados.
- [ ] Verificar que logs não contêm dados proibidos.

### STORY F9-S2 — Titular possui processo de privacidade executável

**Aceite:** políticas refletem o sistema e solicitações podem ser cumpridas com segurança.

#### TASK

- [ ] Resolver inventário, bases e operadores com responsável LGPD.
- [ ] Implementar exportação/correção aplicável.
- [ ] Implementar anonimização preservando obrigações.
- [ ] Implementar preferências/consentimento somente se `PBD-013` aprovar.
- [ ] Documentar procedimento de titular e incidente.
- [ ] Testar anonimização, retenção e restauração com lista de supressão.
- [ ] Publicar privacidade, termos, trocas e entrega aprovados.

## EPIC F10 — Infraestrutura e go-live

### STORY F10-S1 — Staging e Production são isolados e recuperáveis

**Aceite:** falha/alteração em Staging não acessa dados, secrets ou volumes produtivos.

#### TASK

- [ ] Provisionar Ubuntu, usuário, SSH, firewall, Docker e sincronização de tempo.
- [ ] Configurar stacks, redes, bancos, volumes e Data Protection separados.
- [ ] Configurar Nginx, Cloudflare Full strict e origem restrita.
- [ ] Implementar/homologar ForwardedHeaders confiáveis, HTTPS/HSTS, CSP/headers e AllowedHosts por ambiente.
- [ ] Verificar armazenamento persistente no smoke/health sem expor caminhos ou segredos.
- [ ] Proteger Staging por Cloudflare Access e `noindex`.
- [ ] Instalar secrets com permissões mínimas.
- [ ] Configurar limites de recursos, health checks e restart policies.
- [ ] Validar portas públicas e acesso ao banco.

### STORY F10-S2 — Releases são promovidas e revertidas de forma controlada

**Aceite:** artefato homologado é implantado por digest, com migration explícita e smoke.

#### TASK

- [ ] Publicar imagem OCI por SHA/digest no CI.
- [x] Encadear Pages ao CI aprovado do mesmo SHA, via workflow reutilizável; publicar somente push/manual em `main`, sem publicação em PR, com permissões por job (06/10/2026).
- [ ] Implementar deploy de Staging e promoção manual/aprovada.
- [ ] Implementar validação de options e readiness.
- [ ] Implementar etapa explícita de migration.
- [ ] Criar backup pré-deploy e registrar release.
- [ ] Automatizar smoke tests seguros.
- [ ] Ensaiar rollback de imagem backward-compatible.

### STORY F10-S3 — Operação restaura dados após perda

**Aceite:** restore isolado recupera banco e imagens com checksums válidos.

#### TASK

- [ ] Selecionar destino externo e resolver `PBD-014`.
- [ ] Criar `pg_dump`, backup de imagens, manifesto e checksum.
- [ ] Criptografar antes de enviar e aplicar retenção.
- [ ] Monitorar sucesso, falha e espaço.
- [ ] Executar restore trimestral documentado.
- [ ] Medir RPO/RTO e ajustar procedimento.
- [ ] Testar aplicação sobre o estado restaurado.

### STORY F10-S4 — MVP entra em produção com evidência

**Aceite:** checklist de [DEPLOYMENT.md](DEPLOYMENT.md#critérios-de-go-live) está aprovado.

#### TASK

- [ ] Resolver todas as PBDs bloqueadoras de go-live.
- [ ] Executar homologação completa e revisão de conteúdo.
- [ ] Confirmar suporte, políticas, contas e runbooks.
- [ ] Realizar compra produtiva controlada e reembolso quando necessário.
- [ ] Verificar Search Console/sitemap sem ativar marketing não consentido.
- [ ] Abrir tráfego e acompanhar logs, pagamentos e recursos.
- [ ] Registrar decisão, versão e responsáveis pelo go-live.

## EPIC F11 — Evolução pós-MVP

### STORY F11-S1 — Negócio prioriza evolução por evidência

**Aceite:** cada iniciativa tem objetivo, métrica, custo, risco e decisão de privacidade.

#### TASK

- [ ] Coletar feedback operacional e de clientes por canal aprovado.
- [ ] Priorizar gargalos de conversão, suporte e fulfillment.
- [ ] Avaliar analytics consentido e métricas mínimas.
- [ ] Criar story/ADR antes de checkout transparente, novo frete ou storage S3/R2.
- [ ] Avaliar avaliações verificadas, fidelidade e carrinho abandonado separadamente.
- [ ] Medir resultado e remover experimento sem benefício.
