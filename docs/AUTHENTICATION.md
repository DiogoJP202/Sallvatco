<!-- markdownlint-disable MD013 MD060 -->

# Autenticação e contas

## Modelo

ASP.NET Core Identity será usado com `ApplicationUser : IdentityUser<Guid>`. Credencial e perfil comercial são responsabilidades distintas:

- `ApplicationUser` contém dados necessários à autenticação, confirmação, lockout e roles;
- `Customer` contém nome, telefone, e-mail comercial e endereços;
- `Customer.ApplicationUserId` é opcional e único;
- um guest possui `Customer` e pedidos, mas não possui `ApplicationUser`.

Essa separação evita criar senha ou conta implicitamente durante o checkout.

### Estado implementado

- `ApplicationUser : IdentityUser<Guid>` usa e-mail único e confirmado;
- lockout ocorre após 5 falhas por 15 minutos;
- o cookie é `HttpOnly`, essencial, `SameSite=Lax`, seguro fora de Development e isolado pelo nome do ambiente;
- a migration inicial cria `Customer`, `Address` e as roles determinísticas `Customer` e `Admin`;
- o schema Identity v2 foi escolhido para o MVP baseado em senha; passkeys não fazem parte do requisito atual e exigirão decisão e migration próprias se forem adotadas;
- `/Admin` já exige a policy `Admin`, mas nenhuma conta administrativa é criada automaticamente.

Os endpoints de cadastro, confirmação e recuperação permanecem bloqueados pela seleção do provedor em `PBD-010`; tokens ou URLs de confirmação não serão registrados em log como substituto de entrega de e-mail.

## Fluxos de conta

### Cadastro

1. validar e normalizar e-mail;
2. criar usuário sem autenticar como confirmado;
3. associar ou criar `Customer` sem capturar dados além do necessário;
4. enviar token de confirmação por `IEmailSender`;
5. liberar login conforme política de confirmação;
6. após confirmação, oferecer vínculo de pedidos guest do mesmo e-mail.

### Login e logout

- usar cookie Identity, nunca token persistido em local storage;
- exigir e-mail confirmado para acesso à conta;
- renovar security stamp após mudança de senha ou evento de segurança;
- logout usa POST com antiforgery e encerra o cookie;
- aplicar lockout e rate limiting conforme [SECURITY.md](SECURITY.md).

### Recuperação e alteração de senha

- resposta de recuperação é indistinguível para e-mail existente ou inexistente;
- token é curto, de uso único efetivo após alteração e enviado somente por HTTPS;
- nunca registrar token, senha ou URL completa de recuperação;
- alterar senha exige senha atual, exceto no fluxo de reset validado;
- evento invalida sessões existentes quando apropriado.

## Roles e autorização

| Role | Capacidades |
|---|---|
| `Customer` | Gerenciar a própria conta, endereços e pedidos vinculados. |
| `Admin` | Acessar `/Admin`, executar operação e visualizar dados necessários. |

`Manager` não existe no MVP. Policies podem complementar roles para operações sensíveis, mas não devem criar permissões granulares sem necessidade. A conta administrativa inicial é criada por procedimento de deploy, com segredo efêmero fora do código, e deve trocar a senha no primeiro acesso. `PBD-016` define responsáveis e ciclo de acesso.

## Guest checkout

- o e-mail informado pertence ao snapshot do pedido e ao `Customer` guest;
- nenhuma credencial é criada e nenhum e-mail é considerado verificado;
- a página de confirmação não concede acesso permanente ao pedido;
- acompanhamento posterior usa link assinado, aleatório, expirável e enviado ao e-mail, sujeito a `PBD-011`;
- conhecer número do pedido e e-mail não deve bastar para visualizar dados pessoais.

## Vincular pedidos antigos

Pedidos guest só podem ser vinculados depois de o usuário confirmar o mesmo e-mail. O caso de uso:

1. normaliza o e-mail confirmado;
2. localiza `Customer` guest e pedidos ainda sem usuário;
3. exige confirmação explícita do usuário ou executa a associação como consequência documentada da confirmação;
4. registra auditoria técnica da vinculação;
5. não move pedidos já vinculados a outra conta;
6. sinaliza colisões ou divergências para revisão, sem associação automática.

Alterar o e-mail da conta não religa históricos silenciosamente.

## Carrinho e login

O carrinho guest é identificado por token aleatório em cookie. Após login, itens são mesclados com o carrinho da conta, limitados à quantidade permitida e sem preservar preços antigos. Conflitos da mesma variante somam quantidades até o limite de estoque; o checkout recalcula tudo.

## Cookies

- `Secure` obrigatório fora de Development;
- `HttpOnly` para autenticação, carrinho assinado e antiforgery quando aplicável;
- `SameSite=Lax` para autenticação, validando os retornos externos do Checkout Pro;
- duração limitada, sliding expiration apenas para uso legítimo e sessão administrativa mais curta;
- consentimento não é necessário para cookies estritamente essenciais, mas eles devem constar na política.

## Área do cliente

Inclui dados da conta, alteração de senha, endereços e pedidos vinculados. Toda consulta filtra pelo `ApplicationUserId` corrente no servidor; IDs de rota não concedem autorização. Dados de pagamento sensíveis não são exibidos nem armazenados.
