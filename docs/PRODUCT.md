<!-- markdownlint-disable MD013 MD060 -->

# Produto

## Visão

O Sallvat & Co. será o canal digital oficial de uma marca de perfumes artesanais. A experiência deve transmitir identidade, origem e características olfativas dos produtos, ao mesmo tempo em que oferece uma compra confiável, simples e operável por uma equipe pequena.

## Objetivos

- apresentar a marca e o conceito artesanal;
- permitir que visitantes descubram perfumes por características relevantes;
- vender variantes de volume com preço e estoque próprios;
- concluir compras com ou sem conta;
- informar frete, pagamento e andamento do pedido com clareza;
- permitir que um administrador mantenha catálogo, estoque, pedidos, cupons e clientes;
- operar com segurança, rastreabilidade, SEO e baixo custo de infraestrutura.

## Públicos e jornadas

### Visitante

Descobre a marca, navega pelo catálogo, consulta um perfume, seleciona uma variante, calcula frete, monta o carrinho e pode finalizar como convidado.

### Cliente autenticado

Além da jornada de compra, mantém endereços, dados da conta e consulta o histórico de pedidos vinculados a um e-mail confirmado.

### Administrador

Mantém produtos, variantes, imagens, estoque, cupons e pedidos. Toda ação que altere informação comercial ou operacional relevante é auditada.

## Escopo do MVP

- home institucional e páginas de conteúdo;
- catálogo e página de produto com atributos de perfumaria;
- variantes por volume, preço, SKU, dimensões e estoque;
- carrinho persistente para visitante e cliente;
- cupom com regras básicas e validação server-side;
- checkout como convidado ou cliente autenticado;
- cálculo e seleção de frete;
- Mercado Pago Checkout Pro;
- criação, acompanhamento e operação de pedidos;
- cadastro, login, confirmação de e-mail e recuperação de senha;
- área do cliente com pedidos e endereços;
- área `/Admin` para catálogo, estoque, pedidos, clientes e cupons;
- armazenamento local persistente de imagens, preparado para S3/R2;
- logs estruturados, auditoria, backups, LGPD, SEO e deploy em VPS.

## Fora do MVP

- microserviços ou API pública;
- SPA ou aplicativo móvel;
- checkout transparente com captura direta de cartão;
- avaliações de clientes;
- programa de fidelidade;
- recomendação por inteligência artificial;
- recuperação automática de carrinho abandonado;
- relatórios avançados e data warehouse;
- múltiplas moedas, idiomas ou centros de distribuição;
- marketplace ou múltiplos vendedores.

## Estrutura da home

| Seção | Objetivo |
|---|---|
| Hero | Apresentar a proposta central e levar ao catálogo ou lançamento principal. |
| A Sallvat | Comunicar história, intenção e personalidade da marca. |
| Perfumes em destaque | Levar a produtos selecionados manualmente pelo administrador. |
| Lançamentos | Dar visibilidade a novidades sem depender de ordenação cronológica implícita. |
| Conceito artesanal | Explicar processo, cuidado e diferenciais verificáveis. |
| Experiência olfativa | Ajudar o visitante a compreender notas, projeção e fixação. |
| Famílias olfativas | Criar caminhos de descoberta; filtros dependem do catálogo cadastrado. |
| Diferenciais | Comunicar benefícios aprovados pela marca, sem alegações não comprovadas. |
| CTA | Conduzir ao catálogo, produto em destaque ou contato. |
| Instagram | Usar link ou integração conforme `PBD-017`; não bloquear a página por widget externo. |
| Newsletter | Só ativar após definição de provedor, consentimento e double opt-in. |
| Footer | Reunir atendimento, políticas, dados legais e navegação institucional. |

## Rotas de experiência

- `/`, `/perfumes` e `/perfumes/{slug}`;
- `/sobre`, `/contato`, `/privacidade`, `/termos`, `/trocas` e `/entrega`;
- `/carrinho` e `/checkout`;
- `/conta/*` para identidade, endereços e pedidos;
- `/Admin/*` para operação autorizada.

## Indicadores futuros

Indicadores não terão metas inventadas nesta fase. Quando analytics for autorizado, acompanhar disponibilidade, performance, conversão de produto para carrinho, início e conclusão de checkout, falhas de pagamento/frete e pedidos que exigem intervenção.

## Dependências comerciais

O conteúdo final, catálogo, identidade visual, políticas, meios de pagamento, regras de frete e canais de marketing dependem das decisões consolidadas em [REQUIREMENTS.md](REQUIREMENTS.md#pending-business-decisions).
