<!-- markdownlint-disable MD013 MD060 -->

# Segurança

## Recuperação administrativa de pagamentos

`POST /Admin/Pagamentos/{id}/Recuperar` exige policy Admin e antiforgery, versão esperada, motivo tipado e confirmação explícita. Ator vem dos claims; o serviço confere a role atual no banco antes/depois do HTTP. A ação permanece bloqueada com `RecoveryEnabled=false`. Rate limiting nativo após autenticação/autorização: cinco requisições/minuto por usuário/processo, sem fila, 429 na rejeição. Limites de corpo de 8 KiB e de campos do formulário complementam a validação. Não confiar em ID externo, ator, valores ou status enviados pelo navegador.

GET de lista/detalhe não aciona provedor. Histórico administrativo permite apenas enums/identificador de solicitação/data, sem JSON bruto, PII, segredos ou IDs de ator/correlação. Respostas usam no-store e páginas noindex; a área não integra o exportador estático. Revalidação de acesso, auditoria e transação financeira continuam no serviço, independentemente da visibilidade do botão.

## Objetivo e responsabilidade

Segurança é requisito de todas as fases. Controles deste documento devem ser testados antes do go-live e revisados após mudança em autenticação, checkout, upload, integração ou infraestrutura. Não existe confiança implícita em navegador, cookie editável, header do cliente ou payload externo.

## Transporte e proxy

O webhook Orders implementado em 28/09 permanece desligado: HTTPS, JSON até 16 KiB, HMAC em tempo constante, janela de cinco minutos, headers/query únicos, 120 chamadas/minuto por processo e timeout de 18 segundos. Status do corpo não autoriza captura; consulta canônica e commit financeiro são obrigatórios. Segredo e corpo não são registrados. A proteção por processo precisa ser complementada no proxy e homologada antes do uso externo; detalhes em [Pagamentos](PAYMENTS.md).

- Cloudflare usa SSL/TLS `Full (strict)` até o Nginx;
- Nginx expõe apenas 80/443 e redireciona HTTP para HTTPS;
- HSTS é enviado em Production após validação do domínio, com rollout antes de `includeSubDomains`;
- `ForwardedHeaders` confia somente nos proxies conhecidos, não em qualquer origem;
- acesso direto à origem é restringido por firewall quando operacionalmente viável;
- PostgreSQL e Kestrel permanecem em rede interna.

## Cookies e sessão

- cookies de autenticação: `Secure`, `HttpOnly`, `SameSite=Lax` e nome específico por ambiente;
- chaves do ASP.NET Data Protection persistem em volume protegido e são separadas por ambiente;
- cookie administrativo tem duração menor e reautenticação pode ser exigida para operação sensível;
- carrinho usa token aleatório de 256 bits em cookie `HttpOnly`, `Secure` fora do HTTP local e `SameSite=Lax`; somente o hash SHA-256 é persistido, sem dados pessoais ou preço no cookie;
- sessão e cookies são invalidados após reset de senha ou revogação administrativa quando aplicável.

## CSRF, XSS e CSP

- todos os POST/PUT/PATCH/DELETE iniciados pelo navegador exigem antiforgery;
- webhook não usa antiforgery, pois usa assinatura e validação própria;
- Razor mantém encoding automático; `Html.Raw` só com conteúdo estático revisado;
- descrições administrativas são texto ou subconjunto sanitizado, nunca HTML arbitrário;
- CSP inicial: `default-src 'self'`, sem `unsafe-eval`; liberar origens mínimas somente quando uma integração exigir;
- scripts próprios são arquivos versionados ou usam nonce; eventos inline são evitados;
- usar `X-Content-Type-Options: nosniff`, `Referrer-Policy`, `Permissions-Policy` e proteção contra framing.

## SQL Injection e validação

- EF Core usa queries parametrizadas; SQL bruto exige parâmetros e revisão;
- model binding usa view models com allowlist, nunca entidades diretamente;
- validação server-side cobre tamanho, formato, domínio e invariantes;
- ordenação/filtros usam campos permitidos, não nomes SQL vindos do cliente;
- mensagens de banco e stack trace não chegam ao usuário.

## Autenticação e rate limiting

Valores iniciais, ajustáveis após teste de carga:

| Superfície | Política inicial |
|---|---|
| Login | 10 POST por IP a cada 10 minutos; lockout da conta após 5 falhas por 15 minutos. |
| Cadastro | 5 tentativas por IP por hora. |
| Recuperação de senha | 3 por IP e 3 por e-mail normalizado por hora, sem revelar existência. |
| Cotação de frete | 30 por IP por minuto com cache curto. |
| Checkout/criação de pedido | 10 por IP por 10 minutos e idempotência por tentativa. |
| Upload administrativo | 11 MB por multipart, 10 MB por arquivo, 25 megapixels, 10.000 px por dimensão e 10 imagens por produto. |

Rate limiting complementa, mas não substitui Cloudflare, lockout, autorização e idempotência. Webhooks válidos podem chegar em rajada; aplicar limite de corpo e concorrência sem bloquear retries legítimos do provedor.

As três políticas de conta já são aplicadas pelo middleware nativo por IP. Recuperação e reenvio de confirmação também possuem limite em memória por hash do e-mail normalizado; essa proteção é adequada a uma única instância inicial e deverá migrar para estado compartilhado se a aplicação for escalada horizontalmente.

## Administração e autorização

- `/Admin` exige role `Admin` e e-mail confirmado;
- contas são individuais, sem senha compartilhada;
- toda consulta aplica autorização no servidor, inclusive por ID de recurso;
- alteração de preço, estoque, pedido, status, cupom, acesso e imagens gera auditoria;
- ações destrutivas preferem inativação e pedem confirmação contextual;
- conta inicial e revogação seguem runbook e `PBD-016`.

## Uploads

O pipeline implementado segue [STORAGE.md](STORAGE.md): autorização `Admin`, antiforgery, limite antes da alocação, allowlist de extensão, magic bytes, correspondência extensão/conteúdo, decodificação, remoção de metadados, recodificação WebP, chave aleatória, diretório fora do web root e proteção contra decompression bomb/path traversal. Nunca executar ou interpretar conteúdo enviado.

## Webhooks e idempotência

- HTTPS, assinatura, segredo por ambiente e comparação segura;
- consulta canônica ao provedor antes de confirmar valor;
- unique constraints para evento e comando;
- validar referência, ambiente, moeda e total;
- responder a duplicata válida sem repetir efeito;
- guardar payload mínimo/sanitizado e hash quando suficiente;
- rotação de secret com janela controlada e teste no simulador.

## Secrets e configuração

- nenhum segredo no Git, imagem Docker, log ou JavaScript;
- Development usa user-secrets ou variável local;
- Staging/Production usam arquivos/variáveis protegidos com permissão mínima;
- options críticas são validadas no startup sem imprimir valor;
- tokens possuem escopo mínimo e rotação documentada;
- `.env` produtivo não é versionado nem incluído em backup sem criptografia.

O access token do Melhor Envio é lido somente de user-secrets/variável protegida, enviado em `Authorization: Bearer` e nunca incluído na chave de cache, UI ou logs. A integração fica desabilitada quando origem e credenciais não foram homologadas; habilitá-la com token estático não substitui a futura renovação OAuth protegida.

## Logging seguro

Proibido registrar: senha, hash de senha, cookie, token, assinatura, connection string, access/refresh token, CVV, cartão, documento completo, endereço completo, corpo integral de checkout ou URL de reset. E-mail, telefone, CEP e IP são mascarados ou hasheados quando a finalidade permitir. Consulte [OBSERVABILITY.md](OBSERVABILITY.md).

## Backup, restauração e dependências

- backup criptografado, acesso mínimo e cópia fora da VPS;
- restore trimestral documentado em ambiente isolado;
- verificar alertas e atualizações de .NET, NuGet, Node/Tailwind, imagens Docker, Nginx e PostgreSQL;
- aplicar patches de segurança com prioridade e executar testes críticos;
- imagens Docker usam tags imutáveis/digest no deploy produtivo.

## Checklist de incidente

1. preservar logs e correlation IDs sem ampliar exposição;
2. revogar/rotacionar credenciais afetadas;
3. conter rota, conta ou integração;
4. avaliar dados e titulares envolvidos;
5. restaurar serviço a partir de estado conhecido;
6. cumprir avaliação e comunicação LGPD;
7. registrar causa, impacto, correção e prevenção.
