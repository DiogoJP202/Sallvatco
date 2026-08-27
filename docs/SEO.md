<!-- markdownlint-disable MD013 MD060 -->

# SEO e descoberta

## Objetivo

Tornar marca, páginas institucionais e produtos ativos compreensíveis para pessoas e mecanismos de busca, preservando performance e veracidade. SEO técnico não substitui conteúdo aprovado pela Sallvat.

## URLs

| Rota | Indexação |
|---|---|
| `/` | Sim, canonical da home. |
| `/perfumes` | Sim, canonical sem parâmetros de filtro. |
| `/perfumes/{slug}` | Sim quando produto ativo/publicado. |
| `/sobre`, `/privacidade`, `/termos`, `/trocas`, `/entrega` | Sim conforme conteúdo final. |
| `/contato` | Sim se tiver conteúdo útil; formulário protegido. |
| `/carrinho`, `/checkout`, `/conta/*`, `/Admin/*` | `noindex`, sem sitemap. |
| Staging | `noindex, nofollow`, bloqueio de acesso e sitemap não publicado. |

Slugs são minúsculos, ASCII, com hífens e únicos. Mudança de slug publicado registra o anterior e responde 301 para o atual; produto desativado retorna página coerente/404 ou redirect editorial decidido, nunca produto vazio indexável.

## Metadados

- title único, conciso e com marca sem repetição artificial;
- meta description editorial por produto/página, com fallback seguro;
- canonical absoluto HTTPS no domínio oficial;
- Open Graph/Twitter card com imagem otimizada e dimensões conhecidas;
- idioma `pt-BR` e estrutura semântica de headings;
- breadcrumbs visuais e estruturados onde melhorarem navegação.

Não gerar texto, benefícios, avaliações ou disponibilidade que não existam.

## Dados estruturados

Página de produto usa JSON-LD `Product` e `Offer`:

- nome, descrição curta, imagens, SKU da variante/ofertas;
- preço, `BRL`, URL, condição e disponibilidade coerentes com estoque/status;
- variantes representadas de maneira suportada sem duplicar conteúdo;
- marca Sallvat & Co.;
- avaliações somente quando houver avaliações reais e públicas.

Validar com ferramentas de resultados avançados e manter JSON-LD igual ao conteúdo visível.

## Sitemap e robots

- `sitemap.xml` gerado com rotas indexáveis e produtos ativos;
- `lastmod` só muda com alteração relevante;
- `robots.txt` aponta ao sitemap em Production;
- robots não é mecanismo de segurança;
- filtros, ordenações, conta, checkout, admin e webhooks não entram no sitemap;
- ambiente determina host e política, impedindo domínio de Staging em canonical.

## Conteúdo e home

A home segue a estrutura de [PRODUCT.md](PRODUCT.md#estrutura-da-home). Cada seção tem objetivo claro, conteúdo verificável e CTA. Famílias olfativas podem criar navegação/filtros, mas não geram páginas indexáveis vazias. Páginas institucionais precisam de conteúdo final de `PBD-001`, `PBD-002`, `PBD-006`, `PBD-007` e `PBD-017`.

## Imagens e performance

- WebP, `srcset`, dimensões explícitas e compressão conforme [STORAGE.md](STORAGE.md);
- hero prioritário sem lazy loading indevido; imagens abaixo da dobra com lazy loading;
- CSS Tailwind purgado/minificado e JavaScript mínimo/deferido;
- fontes com subset, preload somente quando necessário e fallback adequado;
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
