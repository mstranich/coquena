## Why

`coquena model list` muestra "(removed)" en la columna QUANT cuando un artefacto tiene un fileType que llama.cpp ya no desarrolla activamente (Q4_2, Q4_3, IQ4_NL_4_4, etc.). Estos tipos siguen siendo cuantizaciones válidas y funcionales, especialmente para companion models como projectors, pero el sufijo "(removed)" añade ruido visual y transmite una señal incorrecta: que el tipo no funciona, cuando solo significa que llama.cpp dejó de desarrollarlo.

## What Changes

- Separar los 8 fileType deprecados en un segundo map (`DeprecatedFileTypeNames`) sin el sufijo "(removed)"
- Crear una función de lookup que consulte ambos maps y devuelva el nombre limpio
- Actualizar `computeQuant` y `FormatReference` para usar la función de lookup
- Actualizar tests para reflejar los nuevos nombres sin sufijo

## Capabilities

### Modified Capabilities

- `filetype-quantization-names`: el requirement de incluir tipos removed con sufijo "(removed)" cambia a: tipos deprecados se almacenan en un map separado sin sufijo; el lookup unificado devuelve el nombre limpio para display.

## Impact

- `internal/model/filetypes.go` — split del map en dos
- `internal/model/offline.go` — `FormatReference` usa new lookup
- `cmd/coquena/model.go` — `computeQuant` usa new lookup
- `internal/model/filetypes_test.go` — tests actualizados
