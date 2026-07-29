## Why

`coquena model list` muestra REPOSITORY y FILENAME como columnas separadas, pero el ecosistema llama.cpp usa un nombre lógico `<user>/<repo>:<quant>` de manera casi unívoca. El nombre físico del archivo (ej. `ornith-1.0-9b-Q4_K_M.gguf`) no es un identificador de usuario, y la cuantización es una constante que debe provenir de los metadatos, no del nombre del archivo (el caso `-imatrix` lo demuestra: `model-Q4_K_M.gguf` y `model-Q4_K_M-imatrix.gguf` comparten la misma cuantización).

**Why now**: El change `classify-models-and-companions` ya implementó la gramática `<user>/<repo>:<quant>` internamente (`ModelReference{Raw, Repository, Quant}`), pero la tabla visible no la usa. Los operadores ven dos columnas en vez de una identidad unificada.

## What Changes

- **BREAKING**: La tabla visible de `coquena model list` agrega una columna REFERENCE con el formato `<user>/<repo>:<quant>` como primary key.
- **BREAKING**: Se elimina la columna REPOSITORY (redundante con REFERENCE).
- **BREAKING**: Se mantiene la columna FILENAME para visualizar la variante específica (con o sin imatrix, con o sin calibración, etc.).
- **BREAKING**: Se agrega una constante Go `fileTypeNames` con el mapeo fileType→nombre legible (extraído de `ggml.h` de llama.cpp).
- **BREAKING**: El campo `fileType` del sidecar JSON (entero) se usa para obtener el nombre legible de cuantización, no el nombre del archivo.
- **BREAKING**: Se agrega un archivo constante Go `internal/model/filetypes.go` con el mapeo fileType→nombre.
- **BREAKING**: Los tipos eliminados (Q4_2, Q4_3, Q4_0_4_4, Q4_0_4_8, Q4_0_8_8, IQ4_NL_4_4, IQ4_NL_4_8, IQ4_NL_8_8) se incluyen con nombres descriptivos como "removed".

## Capabilities

### New Capabilities

- `filetype-quantization-names`: Mapeo fileType→nombre legible extraído de llama.cpp. Cada nombre de cuantización es una constante, no derivable del nombre del archivo.

## Impact

- **Code**: `internal/model/filetypes.go` (nuevo), `cmd/coquena/model.go` (modificado), `internal/model/offline.go` (modificado para usar `fileTypeNames`).
- **API**: `LibraryEntry.Source.Filename` sigue siendo accesible internamente; la tabla visible usa `Reference` con `<user>/<repo>:<quant>` como primary key y `Filename` para visualizar la variante específica.
- **Data**: Sidecars existentes no requieren regeneración; el campo `fileType` (entero) es suficiente.
- **Dependencies**: Ninguna nueva dependencia; la tabla se basa en conocimiento existente de llama.cpp.
