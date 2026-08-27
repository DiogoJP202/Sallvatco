<!-- markdownlint-disable MD013 MD060 -->

# Documentação do Sallvat & Co

Esta pasta é a fonte de verdade do projeto. Código, migrations, infraestrutura e operação deverão permanecer coerentes com estes documentos. Quando uma decisão mudar, a documentação e o ADR correspondente devem ser atualizados no mesmo pull request da mudança.

## Ordem de leitura

### Produto e escopo

- [PRODUCT.md](PRODUCT.md) — visão do produto, MVP, pós-MVP e experiência da home;
- [REQUIREMENTS.md](REQUIREMENTS.md) — requisitos rastreáveis, regras conhecidas e decisões comerciais pendentes;
- [SEO.md](SEO.md) — indexação, metadados, dados estruturados e performance de descoberta.

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

## Estado

| Área | Estado |
|---|---|
| Descoberta e documentação | Documentação concluída; aguardando aprovação da Sallvat |
| Desenvolvimento | Fase 1 iniciada: solution e projetos-base criados |
| Homologação | Não iniciado |
| Produção | Não iniciado |
