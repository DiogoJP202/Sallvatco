<!-- markdownlint-disable MD013 MD060 -->

# Documentação do Sallvat & Co

Esta pasta é a fonte de verdade documental do projeto. Código, migrations, infraestrutura e operação devem permanecer coerentes com estes documentos. Quando uma decisão mudar, atualizar documentação e ADR na mesma entrega (commit ou pull request). O fluxo atual autorizado usa commits incrementais em `main`.

## Ordem de leitura

### Situação e continuidade

- [STATUS.md](STATUS.md) — estágio real, arquivos, entregas, lacunas, pendências comerciais e próximos passos;
- [DEVELOPMENT.md](DEVELOPMENT.md) — setup complementar, testes, revisão, commits, push e aceite do CI/Pages;
- [CONFIGURATION.md](CONFIGURATION.md) — configuração existente, flags desativadas, dependências e gaps de implantação;
- [OPERATIONS.md](OPERATIONS.md) — checklist ainda não executado de Staging, homologação e tratamento seguro de exceções.

### Produto e escopo

- [PRODUCT.md](PRODUCT.md) — visão do produto, MVP, pós-MVP e experiência da home;
- [REQUIREMENTS.md](REQUIREMENTS.md) — requisitos rastreáveis, regras conhecidas e decisões comerciais pendentes;
- [SEO.md](SEO.md) — indexação, metadados, dados estruturados e performance de descoberta.
- [IMAGES.md](IMAGES.md) — imagens tratadas da demonstração, arquivos, prompts e limites de uso.

### Arquitetura e domínio

- [ARCHITECTURE.md](ARCHITECTURE.md) — módulos, camadas, dependências e fluxos;
- [DATABASE.md](DATABASE.md) — entidades, relacionamentos, constraints, estoque e concorrência;
- [AUTHENTICATION.md](AUTHENTICATION.md) — Identity, contas, guest checkout e autorização;
- [ORDERS.md](ORDERS.md) — criação, snapshots e máquina de estados;
- [PAYMENTS.md](PAYMENTS.md) — Checkout Pro, webhook, idempotência e reembolso;
- [SHIPPING.md](SHIPPING.md) — cotação, Melhor Envio, etiqueta e rastreamento;
- [STORAGE.md](STORAGE.md) — upload, processamento e armazenamento de imagens.

### Segurança e operação

- [SECURITY.md](SECURITY.md) — controles obrigatórios e ameaças;
- [LGPD.md](LGPD.md) — inventário de dados, minimização, retenção e direitos;
- [OBSERVABILITY.md](OBSERVABILITY.md) — logs, auditoria, correlação e health checks;
- [INFRASTRUCTURE.md](INFRASTRUCTURE.md) — topologia, redes, ambientes, volumes e backups;
- [DEPLOYMENT.md](DEPLOYMENT.md) — build, migração, promoção, rollback e restore;
- [TESTING.md](TESTING.md) — estratégia e cenários críticos.

### Execução e governança

- [ROADMAP.md](ROADMAP.md) — fases, entregáveis, riscos e definição de pronto;
- [BACKLOG.md](BACKLOG.md) — epics, stories e tarefas executáveis;
- [DECISIONS.md](DECISIONS.md) — registro simplificado das decisões arquiteturais.

## Convenções

- `PENDING BUSINESS DECISION` identifica uma decisão que pertence à Sallvat & Co. e não deve ser inventada pelo desenvolvimento.
- As pendências têm identificadores `PBD-xxx` definidos em [REQUIREMENTS.md](REQUIREMENTS.md#pending-business-decisions).
- Valores recomendados são padrões técnicos configuráveis, não regras comerciais definitivas.
- Datas e horários persistidos usam UTC; valores exibidos ao usuário usam o fuso e o formato definidos para a operação brasileira.
- Diagramas Mermaid representam a intenção arquitetural; o código continua sujeito às dependências descritas em texto.
- A seção atual de cada documento e [STATUS.md](STATUS.md) prevalecem sobre registros históricos datados. Planejado não significa implementado; implementado não significa homologado ou produtivo.
- Revisão de código/documentação não é auditoria completa de segurança ou validação jurídica. Evidências devem informar ambiente, data, SHA e limitações.

## Estado

| Área | Estado |
|---|---|
| Descoberta e documentação | Revisada em 01/10/2026; decisões comerciais/aprovação final ainda abertas |
| Desenvolvimento | Núcleo de conta/catálogo/carrinho/pedido implementado; frete e pagamento Sandbox parciais; ver STATUS |
| Demonstração | GitHub Pages publicado, sem backend ou compras |
| Homologação externa | Pendente de Staging, acessos, configuração e trabalho técnico restante |
| Produção transacional | Não liberada; flags externas desligadas e infraestrutura ainda planejada |
