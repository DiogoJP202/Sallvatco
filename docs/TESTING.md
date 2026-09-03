<!-- markdownlint-disable MD013 MD060 -->

# Estratégia de testes

## Objetivo

Testar riscos de negócio, segurança e integração, não perseguir 100% de cobertura. Uma regra crítica deve ter teste rápido quando possível e teste integrado quando depende de transação, banco, autorização ou pipeline HTTP.

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

## Webhook

- payload com assinatura correta é consultado na API fake;
- payload sozinho nunca confirma pedido;
- dez entregas concorrentes do mesmo evento produzem um `WebhookEvent`, um movimento de estoque e uma transição;
- evento aprovado seguido de pendente não regride estado;
- evento desconhecido/valor divergente não altera pedido e gera revisão;
- resposta e logs não contêm segredo.

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
