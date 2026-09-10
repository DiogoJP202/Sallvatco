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

O adapter atual recebe um access token por configuração protegida. Renovação OAuth, persistência protegida de tokens e controle de corrida no refresh serão implementados somente após a estratégia de credenciais ser aprovada. O mesmo vale para o empacotamento: os itens físicos atuais já formam a requisição, mas o uso produtivo aguarda origem e dimensões/peso de embalagem definidos em `PBD-007`.

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

Cada variante publicada exige peso e dimensões do item embalado ou regra de embalagem definida. Não somar dimensões ingenuamente. Até `PBD-007`, o algoritmo de empacotamento será conservador e validado com caixas reais antes da produção. Peso total inclui produtos e embalagem.

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

`PBD-007` e `PBD-008` definem origem, embalagem, serviços, prazo de manuseio, abrangência, frete grátis e retirada.
