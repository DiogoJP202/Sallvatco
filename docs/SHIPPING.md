<!-- markdownlint-disable MD013 MD060 -->

# Frete e envio

## Estratégia

O MVP integra Melhor Envio através de `IFreightService`. O domínio conhece cotação, transportadora, serviço, prazo, preço, etiqueta e rastreio, mas não conhece endpoints ou payloads do provedor. Correios direto e retirada local poderão ser adaptadores futuros.

Referências: [introdução à API](https://docs.melhorenvio.com.br/reference/introducao-api-melhor-envio) e [cotação de fretes](https://docs.melhorenvio.com.br/docs/cotacao-de-fretes).

## Estado da implementação

A fundação de cotação da Fase 6 está disponível, mas permanece desabilitada por padrão:

- o adapter envia produtos para `POST /api/v2/me/shipment/calculate` usando base URL HTTPS por ambiente;
- origem, token, identificação da aplicação, e-mail de suporte, serviços, timeout e validade são options validadas no startup;
- `Authorization: Bearer`, `Accept: application/json` e `User-Agent` com aplicação/contato são aplicados pelo cliente HTTP tipado;
- somente `custom_price` e o prazo customizado válidos viram opções internas em BRL;
- `400/422`, `401/403`, `429`, timeout, transporte e resposta inválida possuem resultados distintos e seguros;
- sucesso é mantido em cache de memória por hash de origem, destino, serviços e itens; falhas não são cacheadas;
- a revalidação ignora o cache, procura a mesma opção e exige nova confirmação se o preço mudar;
- a tela de revisão mostra opções apenas quando há resultado válido e não habilita criação de pedido ou pagamento.

Em 21/09/2026, a segunda fatia passou a aplicar as confirmações comerciais sem depender da caixa maior:

- `Shipping:MelhorEnvio:OriginPostalCode` usa `02320040` e `SupportEmail` usa `sallvatco@gmail.com`; `Enabled` continua `false`, com token vazio;
- `Shipping:Fulfillment:PreparationBusinessDays` configura o prazo médio de preparo (padrão 2, validado entre 0 e 30); a revisão mostra esse período separado do transporte, sem alterar ou somar novamente o prazo retornado pelo provedor;
- enquanto não houver plano de embalagem consolidada validado, somente **uma unidade no total** pode ser cotada automaticamente; duas unidades do mesmo SKU também precisam de caixa maior;
- cotação e revalidação retornam `PackagingRequired`, sem opções ou snapshot de frete, quando há várias unidades; o cliente mantém a sacola e não recebe frete zero como alternativa;
- o adapter também rejeita pedidos de várias unidades antes do cache e da chamada HTTP, protegendo chamadas diretas além do checkout;
- uma cotação anterior de uma unidade não é aceita após aumento de quantidade; a revalidação usa a sacola atual. Voltar para uma unidade permite nova consulta;
- pesos e dimensões de uma unidade continuam vindo da variante cadastrada no servidor. Esta entrega não sobrescreve variantes existentes nem transforma dados demonstrativos em cadastro produtivo.

O usuário autorizou prosseguir sem as medidas da caixa maior. Isso adia a homologação de múltiplos itens, não autoriza medidas estimadas, envio em várias caixas ou frete grátis. A futura criação do pedido ainda deverá guardar o prazo de preparo junto ao snapshot de transporte; a tela atual não cria pedidos nem captura pagamentos.

O adapter atual recebe um access token por configuração protegida. Renovação OAuth, persistência protegida de tokens e controle de corrida no refresh serão implementados somente após a estratégia de credenciais ser aprovada. Os itens físicos atuais já formam a requisição, mas o uso produtivo ainda depende da homologação das embalagens e da configuração real. As confirmações comerciais abaixo não habilitam automaticamente a integração nem alteram o catálogo de produção.

## Dados comerciais confirmados em 21/09/2026

Informações fornecidas pelo usuário, com peso e dimensões lidos nas duas imagens de cadastro e escopo confirmado na conversa:

| Informação | Confirmação |
|---|---|
| CEP de origem | `02320-040` (normalizado para `02320040` na integração) |
| E-mail de contato informado | `sallvatco@gmail.com` |
| Melhor Envio | Conta já cadastrada; autorização da aplicação e credenciais ainda pendentes |
| Preparação e postagem | Prazo médio informado de **2 dias úteis**, separado do transporte |
| Body splash de 200 ml, produto com caixa | **300 g (0,300 kg)**; comprimento **16 cm**, largura **20 cm**, altura **7 cm** |
| Perfume, produto com caixa | **130 g (0,130 kg)**; comprimento **11 cm**, largura **16 cm**, altura **5 cm**; usuário confirmou aplicação a todos os volumes informados (30, 50 e 100 ml) |
| Pedidos com vários produtos | Acondicionamento em **caixa maior**, não remessas individuais como regra |
| Frete grátis | Estratégia ainda não definida; permanece desativado |

Os pesos informados já incluem a caixa individual: não adicionar novamente sua tara na cotação de uma unidade. Para a caixa maior, ainda faltam dimensões externas, peso total real dos conjuntos e capacidade/composição suportada. Não somar dimensões individuais nem assumir que a soma dos pesos embalados representa o peso do volume consolidado. Validar com caixas reais antes de habilitar essa operação.

O prazo da transportadora não inclui os dois dias úteis de preparação. A apresentação futura deve separar essas parcelas; um total estimado só deve ser calculado com uma regra explícita, sem duplicar o manuseio e sem transformar a média informada em garantia de entrega. O marco inicial da contagem, horário de corte e calendário de feriados ainda precisam ser definidos.

O Gmail informado é contato comercial, não confirmação de provedor de e-mails transacionais ou autorização para envio automático. O estado de estoque ilimitado visível nas capturas de outra plataforma não foi solicitado como regra para esta aplicação; o controle de estoque existente permanece.

## Contrato interno

### Entrada de cotação

- CEP de origem configurado e CEP de destino validado;
- itens com quantidade, peso e dimensões físicas;
- valor declarado quando necessário;
- opções configuradas, como mão própria ou seguro, somente se aprovadas.

### Resultado de cotação

- identificador interno/externo da opção;
- transportadora e serviço;
- preço e moeda;
- prazo mínimo e máximo em dias úteis;
- mensagens, restrições e validade da cotação;
- dados suficientes para auditoria sem armazenar payload sensível desnecessário.

Nesta primeira fatia, `IFreightService` implementa cotação. A criação de envio/etiqueta e a consulta de rastreamento ampliarão o limite na `F6-S2`. Erros de cotação já são normalizados como validação, indisponibilidade, autenticação ou limite.

## Cálculo no carrinho e checkout

1. validar CEP no servidor;
2. carregar variantes e dados físicos atuais;
3. montar volumes conforme estratégia de embalagem de `PBD-007`;
4. solicitar cotações com timeout;
5. filtrar respostas inválidas e ordenar conforme UX aprovada;
6. apresentar preço, serviço e prazo sem garantir data absoluta;
7. no checkout, revalidar opção expirada ou incompatível;
8. armazenar snapshot no pedido.

Cotação exibida no carrinho não reserva preço. Cache curto por CEP parcial, conjunto de volumes e configuração pode reduzir chamadas, sem incluir nome ou endereço completo.

## Snapshot no pedido

Guardar transportadora, serviço, preço cobrado, prazo informado, CEP de origem/destino mascarável, identificador da cotação e instante. Mudanças posteriores do provedor não alteram o pedido.

## Peso e dimensões

Cada variante publicada exige peso e dimensões do item embalado ou regra de embalagem definida. As embalagens individuais estão confirmadas acima; a caixa consolidada continua pendente em `PBD-007`. Não somar dimensões ingenuamente. O algoritmo de empacotamento deverá ser validado com caixas reais antes da produção. Peso total inclui produtos, proteção e embalagem de envio.

## Melhor Envio

- separar base URL e credenciais de sandbox/produção;
- usar HTTPS, `Accept`/`Content-Type` JSON e `User-Agent` com aplicação e contato;
- se OAuth2 for adotado, armazenar tokens protegidos, renovar antes da expiração e impedir corrida de refresh;
- considerar que sandbox oferece transportadoras e meios limitados, portanto homologação final precisa de checagem produtiva controlada;
- respeitar limites, `Retry-After` e timeout, evitando retries em criação sem idempotência/consulta;
- não registrar access token, refresh token ou etiqueta completa em logs.

## Compra, etiqueta e postagem

- somente pedidos `Paid` ou `Preparing` podem gerar envio;
- o administrador revisa peso/endereço antes da compra quando necessário;
- criação repetida verifica se `Shipment.ExternalId` já existe;
- etiqueta é armazenada por referência segura, com acesso apenas administrativo;
- marcar `Shipped` exige rastreio/postagem confirmada, não apenas geração da etiqueta;
- cancelamento de etiqueta não cancela automaticamente pedido ou pagamento.

## Rastreamento

`ShipmentStatus` usa estados internos como `Pending`, `LabelCreated`, `Posted`, `InTransit`, `Delivered`, `Exception` e `Cancelled`. Webhook do provedor, se adotado, segue validação e deduplicação equivalentes às de pagamento; caso contrário, job consulta apenas envios não terminais com intervalo e backoff.

Mudanças relevantes geram histórico e podem disparar e-mail idempotente. `Delivered` propõe transição do pedido para `Delivered`; exceções não cancelam ou reembolsam automaticamente.

## Falhas

- CEP inválido: validação antes de chamar o provedor;
- nenhuma cotação: informar indisponibilidade sem zerar frete;
- timeout: permitir tentar novamente e registrar duração;
- preço mudou antes da ordem: pedir nova confirmação ao cliente;
- falha após pagamento: pedido permanece pago e vai para operação/`RequiresAttention`;
- token expirado: renovar uma vez e repetir somente operação segura;
- rastreio desconhecido: preservar último estado e alertar após tentativas.

## Decisões pendentes

`PBD-007` está parcialmente respondida: CEP, embalagens individuais, preparação de dois dias úteis e uso de caixa maior foram confirmados. Faltam endereço completo de postagem, caixas consolidadas e suas capacidades/pesos, transportadoras, regiões atendidas e regras de contagem do preparo. A conexão da conta Melhor Envio continua pendente. `PBD-008` permanece aberta para estratégia de frete grátis, promoções e retirada, sem ativação dessas modalidades.
