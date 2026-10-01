<!-- markdownlint-disable MD013 MD060 -->

# Preparação de homologação e operação

Estado em 01/10/2026: **checklist preparatório, ainda não executado em Staging**. Não há servidor/domínio/contas de teste provisionados por esta entrega. O [deploy](DEPLOYMENT.md) descreve a arquitetura alvo; [CONFIGURATION.md](CONFIGURATION.md) descreve as opções reais e gaps. Não executar operações financeiras reais como smoke test implícito.

## Entradas necessárias

- [ ] Negócio informa VPS/capacidade, domínio Staging/Production e responsável técnico (PBD-015).
- [ ] Responsáveis definem acesso individual, provisionamento Admin e revogação (PBD-016).
- [ ] Provedor/remetente de e-mail transacional e destinatários de teste são aprovados (PBD-010).
- [ ] Contas vendedora/compradora de teste Mercado Pago e autorização Melhor Envio Sandbox são verificadas por canal seguro.
- [ ] Origem, serviços e embalagem de uma unidade são homologados; múltiplas unidades permanecem bloqueadas até caixa validada.
- [ ] Política de reserva, meios de pagamento e devolução é registrada; padrões técnicos não são divulgados como compromisso comercial.
- [ ] Destino de backup, retenção e responsáveis são aprovados (PBD-014).

Credenciais devem ser instaladas diretamente no ambiente protegido. Registro de entrega contém apenas nome da chave, ambiente, responsável e data; não contém valor, screenshot de token ou export de user-secrets.

## Preparar o ambiente antes de homologar

- [ ] Implementar imagem OCI e registry por SHA/digest; stacks e redes isoladas.
- [ ] Configurar DNS/TLS, allowlist de proxies, HTTPS efetivo e headers; PostgreSQL/Kestrel não públicos.
- [ ] Proteger páginas de Staging com Cloudflare Access e noindex. Planejar exceção restrita **apenas** para webhook: provedor não consegue completar login interativo de Access; rota continua exigindo assinatura/validação e proteção própria. Testar sem abrir o restante do ambiente.
- [ ] Configurar volumes de banco, imagens e Data Protection; permissões, backup e rotação de logs.
- [ ] Implementar/provar Admin inicial seguro e e-mail de teste; não promover usuários por SQL improvisado.
- [ ] Revisar SQL/migrations até `AddPaymentRefundDispatch`, aplicar explicitamente e registrar versão.
- [ ] Verificar `/health/live`, `/health/ready`, leitura/escrita de imagem e persistência de sessão/chaves após restart.
- [ ] Confirmar que todas as flags externas começam desligadas e o catálogo contém somente dados de teste aprovados.

## Matriz de homologação

Cada cenário exige resultado esperado, observado, SHA e evidência sanitizada. Reexecutar após correção; não marcar “passou” por ter teste unitário correspondente.

| Jornada | Verificação mínima |
|---|---|
| Conta | Cadastro, confirmação, reset, endereço próprio, Customer sem acesso Admin, sessão expirada e revogação. |
| Catálogo | Publicação/inativação, slug, variantes, preço, estoque, upload válido/malicioso e imagens persistentes. |
| Carrinho/cupom | Guest/login/merge, alteração de preço, estoque concorrente, cupom expirado/limite e replay. |
| Frete | Uma unidade, CEP inválido, sem serviço, timeout/401/429, cotação vencida/preço alterado e duas unidades bloqueadas. |
| Pedido | Revisão vinculada ao dono, duas confirmações, replay, snapshots, última unidade e rollback. |
| Pagamento | Vendedor de teste correto, total/BRL/referência, retorno falso sem aprovação e captura por consulta canônica. |
| Webhook | Assinatura inválida, duplicata, timestamp/reenvio, proxy, corpo excessivo e falha de consulta. |
| Recuperação | ID conhecido, ação Admin, limites 2/5/15 minutos e janela 24 h, disputa com webhook, restart e resposta antiga. |
| Reembolso total | Intenção, envio único, confirmação consultada, duplicata, timeout/queda, sem reposição automática de estoque/cupom. |
| Exceções | Aprovação após expiração, valor divergente, parcial/múltiplas transações e claim sem ID permanecem em revisão. |
| Infra | Restart, perda de conexão, logs sanitizados, restore isolado e rollback compatível com schema. |

Compra de etiqueta, tracking, e-mail de pedido e histórico guest só entram como “aprovados” depois de implementados. A configuração atual não habilita Production do Mercado Pago.

## Procedimentos de exceção

### Pagamento/reembolso incerto

1. Registrar horário, SHA, ID local, estado e correlation ID em canal restrito; não copiar payload/PII.
2. Consultar a fila `/Admin/Pagamentos`. GET lê apenas banco, não resolve divergência.
3. Com ID conhecido e fluxo elegível/habilitado, usar consulta/recuperação explícita; não clicar para gerar outra cobrança.
4. Sem ID, com valor divergente, reembolso parcial ou `RequiresAttention`, interromper envio e encaminhar ao responsável financeiro/técnico. A resolução guiada ainda não existe.
5. Não apagar intenções/recibos, renovar chave, alterar versão/status por SQL ou marcar pago manualmente. Ausência de resposta não prova ausência de operação externa.

### Suspender integração

Desabilitar flags dependentes em conjunto para manter configuração válida, revisar e reiniciar o serviço. Por exemplo, suspender Orders implica manter Checkout, Webhook, Recovery, AutomaticRecovery e ambas as flags de reembolso desligadas. Preservar dados e auditar a mudança. Isso impede novas operações elegíveis, mas não cancela automaticamente uma chamada já enviada ou pagamento no provedor. Planejar recepção/reconciliação de eventos em trânsito; nunca prometer interrupção financeira instantânea.

### Falha de frete ou e-mail

Não substituir falha de frete por custo zero ou embalagem inventada. Preservar carrinho e orientar nova tentativa controlada. E-mail de conta indisponível fora de Development é limitação conhecida; não marcar usuário confirmado para contorná-la. Comunicação transacional de pedido ainda precisa de implementação e política de reenvio.

### Falha de deploy ou migration

Parar promoção, preservar logs sanitizados e backup. Verificar schema realmente aplicado antes de iniciar versão antiga. Downgrade pode falhar após alterações anteriores; não repetir cegamente. Preferir correção aditiva/forward e imagem compatível. Restore ocorre em instância/volume novo antes de qualquer troca; não sobrescrever dados originais. Passos completos em [DEPLOYMENT.md](DEPLOYMENT.md).

## Registro de homologação/release

Preencher por execução, sem incluir segredo ou dado pessoal:

```text
Data/hora e ambiente:
Responsável técnico / aprovador de negócio:
Commit SHA / digest (quando houver):
CI e publicação relacionados:
Versão do schema antes / depois:
Flags alteradas (nomes e estado, sem credenciais):
Cenários executados e evidências sanitizadas:
Falhas, limitações e decisões pendentes:
Backup / checksum / restore isolado:
Compatibilidade e procedimento de rollback:
Resultado: aprovado para teste / reprovado / go-live aprovado separadamente
```

Não há aprovação de go-live neste documento. Todos os requisitos de [STATUS.md](STATUS.md), [BACKLOG.md](BACKLOG.md) e [DEPLOYMENT.md](DEPLOYMENT.md#critérios-de-go-live) precisam de evidência e responsáveis antes de abrir vendas.
