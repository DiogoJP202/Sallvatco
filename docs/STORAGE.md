<!-- markdownlint-disable MD013 MD060 -->

# Armazenamento de imagens

## Estratégia

O MVP usa volume persistente da VPS, fora do web root e fora do PostgreSQL. `IImageStorage` separa a aplicação do meio físico e permite migração para Cloudflare R2, Amazon S3 ou serviço compatível.

O banco armazena apenas chave lógica, metadados, dimensões, tipo, posição e texto alternativo. URL pública é derivada por serviço, não persistida como caminho absoluto do servidor.

## Contrato

`IImageStorage` deve permitir:

- gravar stream sob chave gerada pela aplicação;
- abrir objeto por chave;
- verificar existência;
- excluir objeto por chave após confirmação de que não há referência;
- produzir URL ou endpoint de leitura conforme o adaptador.

O contrato não expõe caminho físico e não assume semântica específica de S3.

## Pipeline de upload

1. exigir usuário `Admin` e antiforgery;
2. limitar request e arquivo antes de carregar em memória;
3. validar extensão permitida e assinatura/magic bytes;
4. decodificar a imagem em biblioteca segura, rejeitando arquivo inválido ou dimensão excessiva;
5. remover metadados EXIF e perfis desnecessários;
6. corrigir orientação;
7. gerar imagem principal WebP e thumbnails com dimensões definidas;
8. usar chave aleatória, sem nome fornecido pelo usuário;
9. persistir arquivos e metadados de forma compensável;
10. auditar inclusão, substituição e remoção.

Tipos de entrada iniciais: JPEG, PNG e WebP. SVG, GIF animado, TIFF, PDF e arquivos executáveis não são aceitos. Limites exatos serão configurados; padrão proposto: 10 MB de upload, 25 megapixels e no máximo 10 imagens por produto, sujeito à validação visual.

## Organização lógica

```text
products/{product-id}/{image-id}/original.webp
products/{product-id}/{image-id}/large.webp
products/{product-id}/{image-id}/thumb.webp
```

IDs são gerados pelo servidor. Caminhos são normalizados e verificados contra traversal. Escrita local usa arquivo temporário no mesmo volume e rename atômico quando possível.

## Entrega

- Nginx pode servir objetos públicos por rota controlada e cache imutável baseado em chave/versionamento;
- imagens não publicadas não devem ser acessíveis por enumeração;
- `Content-Type`, `Content-Length`, `X-Content-Type-Options: nosniff` e política de cache são explícitos;
- HTML usa dimensões, `srcset`, lazy loading fora do hero e texto alternativo editorial;
- substituição cria nova chave para invalidar cache, não sobrescreve silenciosamente.

## Consistência e limpeza

Falha entre storage e banco executa compensação segura. Exclusão de produto não apaga imagens automaticamente. Um relatório periódico identifica órfãos; remoção exige período de carência e registro. Backups do volume seguem a mesma política do banco e restore é testado em conjunto.

## Migração para S3/R2

1. implementar novo adaptador sem alterar casos de uso;
2. copiar objetos preservando chaves e checksums;
3. validar amostra e contagem;
4. alternar leitura com fallback temporário;
5. alternar escrita;
6. remover fallback apenas após janela de verificação.

Credenciais usam acesso mínimo ao bucket, nunca são expostas ao navegador, salvo URLs assinadas quando necessárias.
