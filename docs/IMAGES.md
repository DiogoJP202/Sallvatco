<!-- markdownlint-disable MD013 MD060 -->

# Imagens da apresentação

## Escopo e aprovação

As treze imagens principais foram editadas com a ferramenta integrada de geração de imagens, no modo de edição `precise-object-edit`, a partir dos materiais enviados pelo usuário. Não houve uso de API/CLI de geração externa. A direção visual é fundo marfim, luz suave, frasco inteiro e ausência de preços, textos promocionais ou frascos de outras marcas no entorno.

São **reinterpretações tratadas com IA para demonstração**, não fotografias documentais nem provas de embalagem. A geração pode reconstruir letras pequenas, reflexos e detalhes. A revisão identificou microtextos de rótulo que precisam de validação, inclusive alegações sobre ingredientes e concentração: eles não comprovam características reais do produto e não devem ser reutilizados como texto comercial. Antes de produção, a marca deve aprovar cada rótulo ou fornecer fotografias individuais em alta resolução para substituição. A publicação permanece `noindex`, sem compra real e com aviso explícito de tratamento.

O catálogo mais recente enviado informa **Lumiere**. O nome exibido foi corrigido, mantendo `/perfumes/cumiere/`, SKU e nome de arquivo legados para não quebrar links. Notas olfativas, preços e demais informações do exportador continuam provisórios; esta alteração não os homologa nem cria seed de produção.

## Arquivos finais

As versões anteriores continuam preservadas. Os novos arquivos usam a pasta `packshots`, sem sobrescrever as fontes antigas.

| Produto | Arquivo final relativo à raiz |
|---|---|
| Sea Salt | `tools/Sallvat.Showcase/Assets/products/packshots/sea-salt.webp` |
| Hibernum | `tools/Sallvat.Showcase/Assets/products/packshots/hibernum.webp` |
| Corium | `tools/Sallvat.Showcase/Assets/products/packshots/corium.webp` |
| Lumiere | `tools/Sallvat.Showcase/Assets/products/packshots/cumiere.webp` |
| Body Sea Salt | `src/Sallvat.Web/wwwroot/images/showcase/packshots/body-sea-salt.webp` |
| Body Vanilla Cream | `src/Sallvat.Web/wwwroot/images/showcase/packshots/body-vanilla-cream.webp` |
| Body Golden Freesia | `src/Sallvat.Web/wwwroot/images/showcase/packshots/body-golden-freesia.webp` |
| Body Aqua Imagination | `src/Sallvat.Web/wwwroot/images/showcase/packshots/body-aqua-imagination.webp` |
| Body Gold Bar | `src/Sallvat.Web/wwwroot/images/showcase/packshots/body-gold-bar.webp` |
| Body Sexy Code | `src/Sallvat.Web/wwwroot/images/showcase/packshots/body-sexy-code.webp` |
| Body Code 2 Man | `src/Sallvat.Web/wwwroot/images/showcase/packshots/body-code-2-man.webp` |
| Body Azurra | `src/Sallvat.Web/wwwroot/images/showcase/packshots/body-azurra.webp` |
| Body Savage Force | `src/Sallvat.Web/wwwroot/images/showcase/packshots/body-savage-force.webp` |

Os arquivos principais têm 1120 × 1400 pixels, proporção 4:5, WebP com qualidade 90. Cada body splash também possui um arquivo irmão `-thumb.webp`, de 480 × 600 pixels e qualidade 88. A conversão usa SkiaSharp, sem ampliar os resultados da geração. Os perfumes passam novamente pelo pipeline de upload do exportador, gerando variantes de mídia do catálogo.

`CatalogImagePresentation` informa no `srcset` a largura efetiva calculada pelo pipeline, em vez de declarar 1600 pixels para originais menores. Cards e galeria usam `object-contain` para preservar tampa e base. A troca de miniaturas atualiza `src`, `srcset`, dimensões e texto alternativo. O exportador verifica também os candidatos de `srcset` e `data-srcset`, não apenas `src` e links.

## Verificação desta entrega

- Build Release sem avisos ou erros; 40 testes unitários e 94 de integração aprovados.
- Regressões de `srcset` cobrem originais 399 × 501, 335 × 597 e 1120 × 1400, conferindo os arquivos WebP realmente servidos.
- Exportação estática validada, incluindo os candidatos de imagem responsiva; lint de Markdown e `git diff --check` sem problemas.
- Conferência visual em 390 × 844, 768 × 1024 e 1440 × 1000 sem rolagem horizontal nas páginas examinadas. Nove cards corporais e todas as respectivas imagens carregadas.
- Galeria, menu móvel, filtro olfativo com URL e diálogo de demonstração fechável com Escape verificados no navegador, sem erros de JavaScript detectados.
- Auditoria axe da linha corporal após correção de contraste: zero violações automáticas; contraste de etiquetas sobre imagens ainda requer avaliação visual. Isso não equivale a certificação de acessibilidade nem a pontuação Lighthouse.

## Prompts finais utilizados

Os prompts foram escritos em inglês para edição; os textos da interface permanecem em português. Cada chamada usou a imagem de origem como alvo, inspecionada antes da edição. As instruções de preservação abaixo são objetivos do tratamento, não garantia de fidelidade pixel a pixel.

### Sea Salt

```text
Use case: precise-object-edit. Asset type: clean e-commerce packshot, vertical 4:5.
Image 1 is the EDIT TARGET, the real Sallvat & Co. Sea Salt perfume bottle.
Create a professionally presented, high resolution retouched catalog image using this same bottle. Remove ONLY all advertising outside the bottle: the small unrelated perfume at upper right, the 22% promotional block and paragraph text, top headings, footer and background objects. Preserve the actual bottle design, rectangular transparent glass, transparent faceted rectangular cap, silver atomizer, pale liquid and cream label. Preserve brand lettering Sallvat & Co., SEA SALT, label layout, typography and all existing print; never invent a packaging redesign or extra claims. Do not alter the product identity.
Replace backdrop with a refined seamless warm ivory studio background (#eee6db), gently darker toward bottom. Soft diffused light upper left, realistic contact shadow, restrained glass reflections, no props. Bottle upright centered, fully visible cap and base with comfortable margins, occupies approximately 76 percent of canvas height. Keep entire product sharp, make it clean and presentable, no blur filter, halos, excessive sharpening or glossy CGI look. No text anywhere outside the original label. Output one portrait 4:5 image only.
```

### Hibernum e Corium

Prompt comum, seguido pelo complemento específico:

```text
Use case: precise-object-edit. Image 1 is the EDIT TARGET. Create one clean photorealistic e-commerce packshot on seamless warm ivory (#eee6db) backdrop, soft diffused upper-left light, fine realistic contact shadow. Remove promotional overlay copy and any props/person/other bottles from the scene. Preserve the source bottle geometry, cap, atomizer, liquid color, label design and logo. Keep exact existing label printing, no invented claims or extra text. Preserve source camera angle. Center the complete bottle at 76 percent canvas height, with space above cap and below base, crisp whole product focus, not glossy CGI. Portrait 4:5, high resolution. No text outside original label, no background objects.
```

Hibernum:

```text
Subject: HIBERNUM, amber perfume with clear faceted cap and silver atomizer, exact cream label; bottom source reads ALTA FIXAÇÃO, LONGA DURAÇÃO, MATÉRIAS-PRIMAS SELECIONADAS.
```

Corium:

```text
Subject: CORIUM, pale perfume in transparent glass with clear cap and silver atomizer; preserve exact source label, remove all beach/pedestrian/rocks.
```

### Lumiere

O alvo foi o frasco da terceira linha à direita na primeira folha do catálogo enviado mais recentemente. A primeira tentativa, com grafia incorreta, foi descartada.

```text
Use case: precise-object-edit. Image 1 is EDIT TARGET: supplied catalog sheet. Extract and enhance ONLY the Sallvat & Co. LUMIERE perfume bottle in the THIRD ROW RIGHT (not Golden Freesia to left, not other perfumes). This is a clean catalog packshot, one bottle only.
Keep this specific transparent rectangular bottle with pale pink perfume, gold atomizer and clear rectangular faceted cap, light pink/cream printed label. Preserve the recognizable logo and label layout. Exact primary label text is "Sallvat & Co.", "PERFUMES", "ARTESANO", "LUMIERE", "EAU DE PARFUM"; LUMIERE spelled L U M I E R E, not Eumiere or Cumiere. Keep small source details unchanged; do not invent new certifications/claims.
Remove all sheet layout, prices, page headings, reference brands and unrelated bottles. Seamless warm ivory #eee6db background with soft upper-left light and contact shadow, frontal view, fully visible cap and base, centered at 76 percent image height with margins. High resolution clean photorealistic presentation, portrait 4:5, one bottle, no other objects or any text outside the label.
```

### Linha corporal

Prompt comum para os nove frascos:

```text
Use case: precise-object-edit. Image 1 is EDIT TARGET, real Sallvat & Co. body splash. Create clean photorealistic catalog packshot by editing ONLY scene/background and removing all promotional overlays and other-brand bottle shown at top right. Preserve the actual body spray bottle, proportions, cap, sprayer, liquid level/color and label printing exactly. Do not redesign or translate label. Keep Sallvat & Co. script logo, artwork, exact product name and 200 ml. Seamless warm ivory studio background (#eee6db), soft natural upper-left light, delicate contact shadow. Full bottle centered, upright, cap and base visible, 76 percent of frame height, comfortable margins. Portrait 4:5. No added props or text, no prices or footers anywhere outside original label, no fake packaging.
```

Para `sea-salt`, `vanilla-cream` e `golden-freesia`, complemento com o respectivo identificador no lugar de `{slug}`:

```text
Product: {slug}. Preserve all original label graphics and text from image 1.
```

Para `aqua-imagination`, `gold-bar`, `sexy-code`, `code-2-man`, `azurra` e `savage-force`:

```text
Product: {slug}. Preserve exact label colors, original artwork and wording.
```
