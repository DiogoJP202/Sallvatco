<!-- markdownlint-disable MD013 MD060 -->

# Ficha de aprovação do catálogo piloto

**Sea Salt e Hibernum** são os candidatos para o primeiro piloto de perfumes. Ambos já têm página na vitrine e permitem conferir a seleção de volume, a diferença de preço entre variantes e a correspondência com o feed. Esta ficha registra dados recebidos e campos a completar antes do cadastro real.

## Dados de referência

| Campo | Referência disponível | Validação para o piloto |
| --- | --- | --- |
| Produtos | Sea Salt e Hibernum aparecem no catálogo enviado pela marca | Confirmar os nomes exatos dos rótulos e os volumes vendidos por produto |
| URLs de grupo | `/perfumes/sea-salt` e `/perfumes/hibernum` | Manter os slugs se aprovados e usar o domínio oficial no cadastro do canal |
| Volumes e preços | A tabela enviada informa 30 ml por R$ 45,00, 50 ml por R$ 70,00 e 100 ml por R$ 125,00 | Aprovar os preços atuais e a disponibilidade de cada volume por produto. As quatro linhas abaixo são a seleção proposta para o piloto |
| Peso e medidas por unidade | 130 g, comprimento 11 cm, largura 16 cm e altura 5 cm, incluindo a caixa individual | Usar para uma unidade dos volumes informados. Confirmar medidas externas, peso total e capacidade da caixa maior antes de cotar vários itens |
| Origem e manuseio | CEP 02320-040 e média de dois dias úteis para preparar e postar | Confirmar endereço completo, serviços e regiões atendidas. O transporte tem prazo próprio |
| Atendimento | WhatsApp +55 11 96314-3754, Instagram @sallvatco e `sallvatco@gmail.com` | Conferir na página de contato do domínio oficial |

Os valores de R$ 299,90 e R$ 449,90, os SKUs e os saldos do exportador pertencem à demonstração. A tabela da marca é uma referência comercial recebida e ainda exige confirmação por variante. [Estado do catálogo](STATUS.md#publicado-versus-implementado) · [Dados de envio](SHIPPING.md#dados-comerciais-confirmados-em-21092026)

## Aprovação por produto e variante

| Produto | Família apresentada na vitrine | O que a marca precisa fornecer ou aprovar |
| --- | --- | --- |
| Sea Salt | Fresco aquático | Família, concentração, notas de saída, coração e fundo, descrição curta e longa, projeção, fixação e ocasiões. Foto final e texto alternativo correspondente |
| Hibernum | Âmbar gourmand | Família, concentração, notas de saída, coração e fundo, descrição curta e longa, projeção, fixação e ocasiões. Foto final e texto alternativo correspondente |

Os packshots disponíveis foram tratados para apresentação. Conferir frasco, rótulo, volume, condição do produto e autorização de uso da fotografia final. A capa é compartilhada no feed atual, então deve representar corretamente todas as variantes incluídas. Se a capa não servir a um volume, esse volume fica fora até haver imagem adequada. [Origem e limites das imagens](IMAGES.md#escopo-e-aprovação)

| Produto | Volume candidato | Preço recebido | Preço aprovado | SKU real | Saldo físico inicial | GTIN, MPN ou ausência confirmada | Foto aprovada |
| --- | --- | --- | --- | --- | --- | --- | --- |
| Sea Salt | 50 ml | R$ 70,00 | Pendente | Pendente | Pendente | Pendente | Pendente |
| Sea Salt | 100 ml | R$ 125,00 | Pendente | Pendente | Pendente | Pendente | Pendente |
| Hibernum | 50 ml | R$ 70,00 | Pendente | Pendente | Pendente | Pendente | Pendente |
| Hibernum | 100 ml | R$ 125,00 | Pendente | Pendente | Pendente | Pendente | Pendente |

Registrar o responsável, a data de aprovação e o ID de cada variante após o cadastro. Manter um SKU único por variante e conservar zeros iniciais no GTIN. O ID persistido da variante é usado na seleção por URL e no ID do feed. Quando o fabricante confirmar que os identificadores aplicáveis nunca foram atribuídos, registrar essa decisão para avaliar `NoAssignedIdentifiers`. Aguardar confirmação se o dado estiver apenas desconhecido. [Contrato do feed](SEO.md#feed-e-piloto-merchant-center)

## Conferência do piloto

1. No Admin, cadastrar só volumes aprovados com SKU único, preço BRL, foto e peso e medidas de envio. Registrar saldo físico e conferir se a disponibilidade corresponde ao saldo menos a reserva.
2. Abrir a URL de cada variante com `?variante={id}` e registrar volume, SKU, preço, disponibilidade e imagem. Em homologação, comparar preço unitário e variante selecionada na página com a linha da sacola e da revisão. Comparar o frete separadamente.
3. Ensaiar com dados fictícios uma unidade disponível e uma variante esgotada. Conferir a compra desabilitada para o esgotado e a consistência do estado no JSON-LD e no XML de teste. A disponibilidade real deve vir da operação da loja.
4. Após a liberação comercial, conferir o domínio, checkout, fotos, identificadores, categoria, frete e devolução antes de incluir os IDs aprovados em `Seo:MerchantItems`. Na primeira coleta, comparar `g:link`, `g:canonical_link`, volume, moeda, preço e disponibilidade com a página. Revisar os diagnósticos antes de ampliar a seleção. [Procedimento de ativação](SEO.md#feed-e-piloto-merchant-center)

O piloto está em preparação. Os dados pendentes devem ser aprovados pela marca antes de alimentar catálogo produtivo, feed público ou Merchant Center.
