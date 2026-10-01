<!-- markdownlint-disable MD013 MD060 -->

# Configuração e ambientes

Inventário conferido em 01/10/2026 em `appsettings.json`, `appsettings.Development.json`, `Program.cs` e options de Infrastructure. Nenhum valor secreto deve ser incluído neste documento. Defaults versionados não são prova da configuração efetiva de um servidor.

## Localização e precedência

O host usa a configuração padrão do ASP.NET: arquivos de configuração, user-secrets em Development e overrides de ambiente/linha de comando. Variáveis usam `__` entre níveis. Evitar segredo em argumento de processo, histórico de terminal ou logs; provisionar por canal protegido. User-secrets são conveniência local, não cofre produtivo. Ver [DEVELOPMENT.md](DEVELOPMENT.md) e [SECURITY.md](SECURITY.md).

| Chave de ambiente | Default/comportamento | Ação necessária |
|---|---|---|
| `ASPNETCORE_ENVIRONMENT` | Perfil local define Development. | Staging/Production explícitos; não usar Development externamente. |
| `ConnectionStrings__SallvatDatabase` | Sem conexão versionada; obrigatória. | Credencial separada por ambiente; não logar. |
| `AccountLinks__PublicOrigin` | Local `http://localhost:5170`; obrigatória e HTTPS fora de Development/Testing. | Origem canônica sem caminho/query, domínio do ambiente. |
| `DataProtection__KeysPath` | Local `.local/data-protection-keys/development`, resolvido pelo content root. | Volume persistente absoluto fora do web root, acesso mínimo e backup protegido. |
| `ImageStorage__RootPath` | Local `.local/images/development`. | Volume separado e persistente, fora do web root. |
| `ImageStorage__PublicPath` | `/media`. | Manter coerente com proxy e URLs. |
| `Orders__ReservationMinutes` | 30; aceita 5–1440. | Padrão técnico sujeito a PBD-005. |
| `Shipping__Fulfillment__PreparationBusinessDays` | 2; aceita 0–30. | Média de preparo/postagem confirmada, não prazo total de entrega. |
| `AllowedHosts` | `*`. | Restringir ao host esperado na implantação. |
| `Operational__ServiceName` / `CorrelationIdMaxLength` | `Sallvat.Web` / 64. | Identificar serviço e manter limite entre 16–128. |

Limites atuais de imagem: 10 MiB, 25 milhões de pixels, dimensão 10.000 px e 10 imagens por produto; multipart 11 MiB. Ver [STORAGE.md](STORAGE.md). Volumes de imagens e chaves não são públicos como diretórios físicos; `/media` serve os objetos. Chave aleatória não substitui autorização se futuramente houver conteúdo privado. Persistência do key ring não implica criptografia em repouso: o exportador local avisa que não há XML encryptor. Definir proteção de disco/chaves e cópia criptografada antes da implantação.

## Mercado Pago

Prefixo de todas as chaves abaixo: `Payments__MercadoPago__`. **Todas as flags são `false` por padrão.** A configuração atual só aceita `Environment=Sandbox`, inclusive quando desabilitada. Produção demanda implementação, revisão e homologação, não apenas substituir credenciais.

| Chave | Função e dependências |
|---|---|
| `Enabled` | Adapter legado Preferences; não habilitar junto com Orders. Não é a flag geral de todas as integrações. |
| `OrdersEnabled` | Criação/consulta Orders; exige vendedor de teste e configuração válidos. |
| `WebhookEnabled` | Requer Orders e segredo válido de webhook. |
| `RecoveryEnabled` | Requer Orders; recuperação manual por consulta, nunca reenvio financeiro. |
| `AutomaticRecoveryEnabled` | Requer Recovery; job de consultas limitado. |
| `CheckoutEnabled` | Requer Orders, Webhook, Recovery e AutomaticRecovery; caso de uso também exige frete Sandbox configurado. |
| `RefundPreparationEnabled` | Requer Orders e Webhook; prepara intenção local. |
| `RefundEnabled` | Requer preparação habilitada; autoriza dispatcher total Sandbox, não confirmação sem consulta. |
| `AccessToken` | Secreto, vazio no repo; token válido de conta de teste, sem espaços/controle. |
| `WebhookSecret` | Secreto, vazio no repo; 32–256 caracteres sem espaços/controle quando habilitado. |
| `PublicOrigin` | Origem HTTPS DNS pública, sem caminho/query/credenciais, porta padrão; não usar Pages como backend. |
| `TestSellerId` / `TestSellerConfirmed` | `0` / `false`; identificar e confirmar a conta de teste de verdade, não inventar IDs. |
| `TimeoutSeconds` | 10; aceita 2–30. Não habilita retry de POST. |

O estado seguro preserva flags falsas e segredos vazios. Habilitar somente depois do checklist de [OPERATIONS.md](OPERATIONS.md), por etapa explícita em servidor de testes. Validar combinações com testes e startup sem expor valores. Não usar esta tabela como script de ativação automática.

## Melhor Envio

Prefixo `Shipping__MelhorEnvio__`:

| Chave | Default / observação |
|---|---|
| `Enabled` | `false`. |
| `BaseUrl` | `https://sandbox.melhorenvio.com.br/`. |
| `OriginPostalCode` | `02320040`, confirmado. |
| `SupportEmail` / `ApplicationName` | `sallvatco@gmail.com` / `Sallvat`; identificação HTTP, não provedor de e-mail. |
| `AccessToken` | Vazio; secreto, configurado fora do Git. Refresh OAuth ainda não implementado. |
| `ServiceIds` | Vazio; lista final depende da operação. |
| `TimeoutSeconds` / `CacheSeconds` / `QuoteValiditySeconds` | 10 / 120 / 600. |

Habilitação não remove o bloqueio de múltiplas unidades. Peso/dimensões vêm da variante cadastrada; os números fornecidos não são seed produtivo automático. Uma conta existente no Melhor Envio não comprova autorização OAuth nem adequação dos serviços.

## E-mail, identidade e jobs

Não existe configuração pronta de SMTP/Resend/outro provedor. `IEmailSender` usa arquivo em Development (`.local/emails`) e implementação explicitamente indisponível nos outros ambientes. Não ativar e-mail real com senha de Gmail por improviso.

Identity usa cookie de 8 horas com sliding expiration, lockout após 5 falhas por 15 minutos e tokens de 3 horas. Não há sessão Admin mais curta implementada, bootstrap de Admin, MFA ou gestão operacional completa de acesso. Nomes de cookies e application name do Data Protection separam ambientes, mas volumes/credenciais também precisam ser separados.

Jobs registrados em `Program.cs`: limpeza de carrinhos, expiração de pedidos e recuperação de pagamento. A recuperação externa depende das flags; os jobs locais não dependem delas. Antes de ligar um servidor sobre dados existentes, avaliar expiração/limpeza. Não apontar testes ou demonstração para banco produtivo.

## Gaps de implantação

O projeto ainda não contém middleware global de CSP/HSTS/HTTPS redirection/ForwardedHeaders nem configuração Nginx produtiva. O validador exigir URLs HTTPS não configura proxy ou transporte. A identidade depende de HTTPS efetivo e origem confiável; rate limiting por IP exige proxy configurado corretamente. Readiness testa PostgreSQL, não leitura/escrita do volume nem credenciais externas. Esses pontos permanecem trabalho de F9/F10, não garantias presentes.
