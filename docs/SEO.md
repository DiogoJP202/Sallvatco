<!-- markdownlint-disable MD013 MD060 -->

# SEO e descoberta

O primeiro lançamento indexável será a loja homologada no domínio próprio. As opções `Seo:IndexingEnabled` e `Seo:MerchantFeedEnabled` começam desligadas. Antes de liberar, conferir domínio e TLS, catálogo e políticas aprovados, compra produtiva homologada, backup restaurável e CI verde. Ver [STATUS.md](STATUS.md).

## Objetivo

Tornar marca, páginas institucionais e produtos ativos compreensíveis para pessoas e mecanismos de busca, preservando performance e veracidade. SEO técnico não substitui conteúdo aprovado pela Sallvat.

## URLs

| Rota | Indexação |
|---|---|
| `/` | Sim, canonical da home. |
| `/perfumes` | Sim quando contém produtos. `?pagina=N` tem canonical próprio a partir da segunda página. |
| `/perfumes?familia=…` | `noindex`, canonical da seleção normalizada, fora do sitemap. |
| `/perfumes/{slug}` | Sim quando produto ativo/publicado. |
| `/linha-corporal` | `noindex` enquanto nomes, valores e conteúdo aguardarem validação comercial. |
| `/sobre`, `/entrega`, `/contato` | Elegíveis após a liberação do domínio próprio. |
| `/privacidade`, `/termos`, `/trocas` | Dependem de conteúdo aprovado e de rotas publicadas. |
| `/carrinho`, `/checkout`, `/conta/*`, `/Admin/*` | `noindex`, sem sitemap. |
| Staging | `noindex, nofollow`, bloqueio de acesso e sitemap não publicado. |

Página fora do intervalo, filtro inexistente e parâmetro funcional malformado ou repetido retornam 404 com navegação. A página 1 explícita e aliases de grafia inequívocos redirecionam em um salto. Slug histórico preserva variante válida e parâmetros de campanha reconhecidos. A variante tem URL GET própria e canonical do grupo. Seleção inválida devolve 404. Esgotamento temporário mantém o produto acessível e arquivamento devolve 404.

## Metadados

- title único, conciso e com marca sem repetição artificial;
- meta description editorial por produto/página, com fallback seguro;
- canonical absoluto HTTPS no domínio oficial;
- Open Graph/Twitter card com imagem otimizada e dimensões conhecidas;
- idioma `pt-BR` e estrutura semântica de headings;
- breadcrumbs visuais e estruturados onde melhorarem navegação.

Não gerar texto, benefícios, avaliações ou disponibilidade que não existam.

O MVC centraliza canonical, elegibilidade e metadados sociais usando `AccountLinks:PublicOrigin`. O layout só permite indexar rotas públicas declaradas em Production com liberação explícita. Produtos têm breadcrumb visível, links para imagens secundárias, relacionados da mesma família e acesso a entrega e contato.

## Dados estruturados

O produto de volume único usa `Product`. Vários volumes distintos usam `ProductGroup` e um `Product` por variante. Ofertas aparecem somente na publicação aprovada. IDs das entidades usam os identificadores persistidos, e preço, moeda e disponibilidade vêm do catálogo público. A home usa `WebSite` e `Organization`, com `OnlineStore` na liberação. O Instagram confirmado é referência de identidade, e telefone e e-mail são canais de contato. Logo, atributos adicionais e políticas estruturadas aguardam dados aprovados. [Variantes](https://developers.google.com/search/docs/appearance/structured-data/product-variants) · [Listagens comerciais](https://developers.google.com/search/docs/appearance/structured-data/merchant-listing)

- nome, descrição curta, imagens, SKU da variante/ofertas;
- preço, `BRL`, URL, condição e disponibilidade coerentes com estoque/status;
- variantes representadas de maneira suportada sem duplicar conteúdo;
- marca Sallvat & Co.;
- avaliações somente quando houver avaliações reais e públicas.

Validar com ferramentas de resultados avançados e manter JSON-LD igual ao conteúdo visível.

## Sitemap e robots

`/sitemap.xml` lista home, Sobre, Contato, Entrega, catálogo preenchido e detalhes canônicos publicados, incluindo esgotados temporariamente. Filtros, paginação, variantes, conta, checkout, Admin, webhooks e linha corporal não integram o XML. Acima de 50 mil URLs, usa índice e partes na raiz. Há cache interno de até cinco minutos, ETag e resposta 304. Falha de banco responde 503. `lastmod` fica omitido até haver uma fonte confiável de revisão pública. Datas de reserva, ajuste de estoque e destaque não servem como datas editoriais.

Development, Testing e Staging emitem `Disallow: /` e `noindex`, com sitemap indisponível. Staging mantém autenticação e header na borda. Production bloqueada permite ler o `noindex` das páginas públicas. Quando liberada, anuncia o sitemap. O Pages mantém `noindex,nofollow` em todo HTML e omite ofertas. O arquivo robots exportado sob `/Sallvatco/` não governa a raiz do host.

Na borda de produção, permitir Googlebot, Bingbot, OAI-SearchBot, PerplexityBot e os recursos públicos. `GPTBot` e `ClaudeBot` têm grupos de bloqueio. `Google-Extended` permanece permitido. Conferir regras de WAF, bots e robots gerenciado. Exceções de rastreamento devem usar identidade verificada e escopo público. [Google](https://developers.google.com/crawling/docs/crawlers-fetchers/google-common-crawlers) · [OpenAI](https://help.openai.com/en/articles/12627856-publishers-and-developers-faq) · [Cloudflare](https://developers.cloudflare.com/ai-crawl-control/configuration/ai-crawl-control-with-waf/)

## Feed e piloto Merchant Center

`/feeds/google-merchant.xml` exige Production liberada e `Seo:MerchantFeedEnabled=true`. Cadastrar em `Seo:MerchantItems` somente IDs de variantes com fotos e condição de item novo revisadas. Por ID, informar `Gtin` ou `Mpn` reais, ou `NoAssignedIdentifiers=true` após confirmar a inexistência. O mapa permite operar o piloto sem alteração do Admin ou do banco.

O feed usa IDs externos persistentes, seleção de variante, canonical do grupo, volume, preço BRL e disponibilidade. Itens arquivados, inativos, de demonstração e grupos com volumes ambíguos não devem integrar o piloto. A geração registra contagem, exclusões e duração. O cache é interno e dura até cinco minutos.

1. Confirmar domínio, catálogo, identificadores, imagens, categoria, frete e devolução para dois produtos piloto. Validar no Merchant Center os requisitos de compra direta com o pagamento efetivamente usado.
2. Configurar coleta diária do XML e validar preço, estoque, frete, HTML, JSON-LD e checkout por variante. Atualizações automáticas do Merchant complementam a coleta.
3. Revisar diagnósticos diariamente na primeira semana. Investigar 48 horas sem coleta, queda inesperada no número de itens ou divergências. Considerar Merchant API se a frequência de mudança justificar. [Especificação](https://support.google.com/merchants/answer/7052112?hl=pt-BR) · [Listagens gratuitas](https://support.google.com/merchants/answer/13889434)

## Conteúdo e home

A home segue a estrutura de [PRODUCT.md](PRODUCT.md#estrutura-da-home). Cada seção tem objetivo claro, conteúdo verificável e CTA. Famílias olfativas podem criar navegação/filtros, mas não geram páginas indexáveis vazias. Páginas institucionais precisam de conteúdo final de `PBD-001`, `PBD-002`, `PBD-006`, `PBD-007` e `PBD-017`.

Pesquisar intenção no Brasil por SERP, Trends, atendimento e Search Console. A expansão começa com até três guias aprovados, conectados aos produtos reais. Uma categoria exige intenção distinta, conteúdo original e pelo menos dois produtos relevantes. `llms.txt` segue fora do roadmap até haver uso documentado e hipótese mensurável.

## Imagens e performance

- WebP, `srcset`, dimensões explícitas e compressão conforme [STORAGE.md](STORAGE.md);
- hero prioritário sem lazy loading indevido; imagens abaixo da dobra com lazy loading;
- CSS Tailwind purgado/minificado e JavaScript mínimo/deferido;
- fontes do sistema, sem download adicional;
- cache longo para assets versionados;
- evitar embeds sociais que prejudiquem Core Web Vitals e privacidade;
- páginas de produto devem funcionar sem JavaScript para conteúdo e compra essencial.

## Erros e redirects

- 404 real para rota inexistente, com navegação útil;
- 301 para slug histórico e mudança institucional permanente;
- 302/307 apenas temporário;
- não redirecionar toda página ausente para a home;
- paginação fornece links rastreáveis e canonical coerente;
- parâmetros de campanha são removidos do canonical.

## Verificação

- validar HTML semântico, headings, canonical e JSON-LD;
- testar sitemap/robots por ambiente;
- executar Lighthouse e teste de performance em dispositivos móveis;
- verificar páginas sem JS e com imagens indisponíveis;
- após go-live, configurar Search Console somente com conta aprovada;
- analytics/pixels permanecem sujeitos a `PBD-013`.

No staging, medir cinco cargas frias por template e registrar mediana, elemento LCP, TTFB, bytes e CLS. Testar teclado, leitor de tela, zoom, galeria e seleção em 390, 768 e 1440 px e um celular real. Na borda, conferir compressão, cache apenas de assets versionados, acesso dos crawlers e um salto de redirect para o HTTPS canônico. Acompanhar LCP ≤ 2,5 s, INP ≤ 200 ms e CLS ≤ 0,1 no p75 quando houver dados de campo. [Web Vitals](https://web.dev/articles/vitals)

Após aprovação, verificar domínio em Search Console e Bing, enviar sitemap e inspecionar amostras. Rever semanalmente no primeiro mês. Alertar para `noindex` inesperado, erro de sitemap, canonical fora do domínio ou 5xx recorrente. Criar baseline de quatro semanas e comparar períodos de 28 dias por consulta e página. Observar relatórios de IA e referências identificáveis. Até a aprovação de analytics, usar dados agregados de aquisição e operação sem atribuir pedidos a canais.
