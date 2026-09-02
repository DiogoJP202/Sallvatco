<!-- markdownlint-disable MD013 MD024 MD060 -->

# Backlog executável

Este backlog segue o [ROADMAP.md](ROADMAP.md). `EPIC` corresponde a uma fase/resultado, `STORY` descreve valor verificável e `TASK` é unidade de execução. Itens da Fase 0 já materializados estão marcados; aprovação comercial permanece aberta.

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
- [ ] Implementar cadastro com view model allowlisted.
- [ ] Implementar confirmação de e-mail e expiração de token.
- [ ] Implementar login, logout POST e lockout.
- [ ] Implementar solicitação/reset de senha sem enumeração.
- [ ] Implementar alteração de senha e invalidação de sessão.
- [ ] Integrar `IEmailSender` ao provedor definido em `PBD-010`.
- [ ] Testar cookies, antiforgery, rate limits e fluxos de erro.

### STORY F2-S2 — Cliente gerencia perfil, endereços e histórico vinculado

**Aceite:** apenas o titular confirmado acessa e altera seus dados.

#### TASK

- [x] Criar entidades/configurações `Customer` e `Address`.
- [x] Criar migration, índices e constraints.
- [ ] Implementar criação/associação de perfil durante cadastro e guest checkout.
- [ ] Implementar CRUD de endereços com autorização por recurso.
- [ ] Criar páginas de conta e pedidos ainda vazias/contratadas.
- [ ] Implementar vínculo de pedidos guest após confirmação do e-mail.
- [ ] Tratar colisão com pedido já vinculado sem transferência automática.
- [ ] Testar IDOR, alteração de e-mail e vínculo guest.

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

- [ ] Criar entidades `Product` e `ProductVariant` e invariantes.
- [ ] Configurar mappings, precisão, índices únicos, checks e concorrência.
- [ ] Criar migration e dados de teste não produtivos.
- [ ] Implementar casos de uso de criar, editar, publicar, inativar e destacar.
- [ ] Criar controllers/views Admin com validação e antiforgery.
- [ ] Impedir publicação sem preço, SKU, peso, dimensões e imagem necessários.
- [ ] Auditar preço, status e alteração comercial.
- [ ] Testar slug/SKU duplicado, overposting e conflito de edição.

### STORY F3-S2 — Administrador gerencia imagens seguras e otimizadas

**Aceite:** somente imagens válidas geram WebP/thumbnails fora do web root.

#### TASK

- [ ] Criar `ProductImage` e configuração.
- [ ] Definir e implementar `IImageStorage` local.
- [ ] Selecionar biblioteca de imagem após revisão de licença e segurança.
- [ ] Validar tamanho, extensão, magic bytes, dimensões e decodificação.
- [ ] Remover metadados e gerar variantes WebP.
- [ ] Implementar upload, ordenação, capa e remoção compensável.
- [ ] Configurar entrega/cache/headers no ambiente local.
- [ ] Testar arquivo disfarçado, bomba de dimensão, traversal, órfão e concorrência.

### STORY F3-S3 — Estoque é ajustado com histórico e concorrência

**Aceite:** saldo nunca fica abaixo do reservado e todo ajuste tem motivo/ator.

#### TASK

- [ ] Criar `InventoryMovement` e campos `OnHand`, `Reserved`, versão.
- [ ] Implementar ajuste condicional de estoque.
- [ ] Criar tela Admin de saldo e movimentos.
- [ ] Exigir justificativa em ajuste manual.
- [ ] Auditar antes/depois e movimento resultante.
- [ ] Testar conflito, quantidade inválida e redução abaixo do reservado.

### STORY F3-S4 — Visitante descobre produtos indexáveis

**Aceite:** catálogo e produto funcionam sem JS e só expõem conteúdo publicado.

#### TASK

- [ ] Implementar query paginada de catálogo e filtros essenciais.
- [ ] Implementar página `/perfumes/{slug}` com seleção de variante.
- [ ] Implementar home e destaques conforme conteúdo aprovado.
- [ ] Gerar title, description, canonical, Open Graph e JSON-LD.
- [ ] Implementar 404 e histórico/redirect 301 de slug.
- [ ] Aplicar imagens responsivas e acessibilidade básica.
- [ ] Testar produto inativo, sem estoque, slug antigo e metadados.

## EPIC F4 — Carrinho e cupons

### STORY F4-S1 — Visitante mantém carrinho confiável

**Aceite:** itens persistem sem confiar em preço ou estoque do navegador.

#### TASK

- [ ] Criar `Cart` e `CartItem`, mappings, índices e migration.
- [ ] Gerar token guest aleatório e cookie seguro.
- [ ] Implementar adicionar, alterar quantidade, remover e limpar.
- [ ] Recalcular catálogo, preço e disponibilidade em cada resumo relevante.
- [ ] Implementar carrinho autenticado e mesclagem após login.
- [ ] Implementar expiração e limpeza em lote.
- [ ] Criar views responsivas e mensagens de alteração de preço/estoque.
- [ ] Testar cookie adulterado, variante inativa, limite de quantidade e mesclagem.

### STORY F4-S2 — Administrador cria cupom com limites verificáveis

**Aceite:** cupom válido aplica desconto determinístico e não ultrapassa limite concorrente.

#### TASK

- [ ] Criar `Coupon` e `CouponRedemption` com invariantes.
- [ ] Criar mappings, índices, checks e migration.
- [ ] Implementar cálculo central de desconto e rateio.
- [ ] Implementar CRUD Admin, ativação e auditoria.
- [ ] Aplicar padrão não acumulável enquanto `PBD-009` estiver pendente.
- [ ] Implementar reserva/consumo/liberação de limite junto ao pedido.
- [ ] Testar expiração, mínimo, uso por cliente/e-mail, corrida e cancelamento.

## EPIC F5 — Checkout e pedidos

### STORY F5-S1 — Guest ou cliente informa comprador e entrega

**Aceite:** checkout coleta apenas dados necessários e valida tudo no servidor.

#### TASK

- [ ] Criar view models em etapas ou formulário único conforme teste de UX.
- [ ] Implementar validação e normalização de contato/CEP/endereço.
- [ ] Pré-preencher dados autenticados sem exigir salvamento.
- [ ] Não coletar CPF enquanto `PBD-003` não exigir.
- [ ] Implementar confirmação das políticas aplicáveis sem checkbox abusivo.
- [ ] Testar overposting, campos ausentes, endereço de outro cliente e guest.

### STORY F5-S2 — Sistema cria pedido e reserva estoque atomicamente

**Aceite:** pedido completo ou nenhum efeito; concorrência não causa overselling.

#### TASK

- [ ] Criar `Order`, `OrderItem`, `OrderAddress` e estados.
- [ ] Criar `StockReservation` e configurações/migration.
- [ ] Implementar calculador único de totais.
- [ ] Implementar snapshots de item, contato, endereço, cupom e frete.
- [ ] Implementar update condicional de reserva em ordem estável.
- [ ] Gerar número público único e expiração configurável.
- [ ] Tornar criação idempotente por tentativa de checkout.
- [ ] Limpar/associar carrinho somente após sucesso.
- [ ] Testar rollback, última unidade concorrente, total adulterado e snapshot.

### STORY F5-S3 — Pedidos expiram e transitam de forma controlada

**Aceite:** apenas transições documentadas ocorrem e repetição não duplica efeito.

#### TASK

- [ ] Implementar máquina de estados no domínio.
- [ ] Implementar job de expiração em lotes.
- [ ] Liberar reserva/cupom idempotentemente.
- [ ] Criar casos de uso Admin com versão e justificativa.
- [ ] Criar `RequiresAttention` e fila de resolução.
- [ ] Auditar transições manuais.
- [ ] Testar todas as arestas válidas e inválidas.

## EPIC F6 — Frete e Melhor Envio

### STORY F6-S1 — Cliente recebe cotações válidas por CEP

**Aceite:** opções refletem itens físicos e falha nunca resulta em frete gratuito acidental.

#### TASK

- [ ] Definir DTOs internos e `IFreightService`.
- [ ] Implementar cliente Melhor Envio com options, timeout e `User-Agent`.
- [ ] Implementar autenticação/refresh conforme credencial aprovada.
- [ ] Implementar algoritmo de embalagem validado com `PBD-007`.
- [ ] Implementar cotação e cache curto sem PII excessiva.
- [ ] Revalidar opção no checkout e tratar mudança de preço.
- [ ] Persistir snapshot no pedido/shipment.
- [ ] Testar CEP, nenhuma cotação, timeout, 401/429 e sandbox.

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

### STORY F7-S1 — Cliente é redirecionado para uma preferência idempotente

**Aceite:** pedido local existe antes do gateway e timeout não cria cobrança duplicada.

#### TASK

- [ ] Definir `IPaymentGateway` e resultados internos.
- [ ] Criar `Payment`, mappings, índices e migration.
- [ ] Implementar cliente Checkout Pro com options por ambiente.
- [ ] Criar preferência com referência, valores e URLs HTTPS.
- [ ] Persistir idempotency key, preferência e falhas sanitizadas.
- [ ] Implementar retry seguro e consulta após timeout ambíguo.
- [ ] Criar páginas de retorno não autoritativas.
- [ ] Testar payload, ambiente, timeout, retry e URL.

### STORY F7-S2 — Webhook confirma pagamento uma única vez

**Aceite:** assinatura + consulta canônica confirmam exatamente um pagamento/estoque.

#### TASK

- [ ] Criar `WebhookEvent`, unique constraints e migration.
- [ ] Implementar endpoint com limite de corpo e sem antiforgery.
- [ ] Validar `x-signature` e segredo por ambiente.
- [ ] Deduplicar evento antes de efeitos.
- [ ] Consultar pagamento e validar referência, valor, moeda e ambiente.
- [ ] Aplicar `PaymentStatus`, `OrderStatus` e estoque na mesma transação.
- [ ] Responder corretamente a duplicata, falha transitória e payload inválido.
- [ ] Testar concorrência, ordem de eventos e ausência de segredo nos logs.

### STORY F7-S3 — Operação concilia e reembolsa pagamento

**Aceite:** divergência é visível e reembolso só altera pedido após confirmação.

#### TASK

- [ ] Implementar job de conciliação de pendentes/eventos falhos.
- [ ] Implementar solicitação de reembolso total idempotente.
- [ ] Implementar confirmação do reembolso e transição.
- [ ] Tratar aprovação tardia com nova reserva ou `RequiresAttention`.
- [ ] Criar tela Admin de tentativas, divergências e retry seguro.
- [ ] Auditar reembolso e resolução manual.
- [ ] Testar falha/retry, valor divergente e estoque indisponível.

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
- [ ] Proteger Staging por Cloudflare Access e `noindex`.
- [ ] Instalar secrets com permissões mínimas.
- [ ] Configurar limites de recursos, health checks e restart policies.
- [ ] Validar portas públicas e acesso ao banco.

### STORY F10-S2 — Releases são promovidas e revertidas de forma controlada

**Aceite:** artefato homologado é implantado por digest, com migration explícita e smoke.

#### TASK

- [ ] Publicar imagem OCI por SHA/digest no CI.
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
