# Troubleshooting

Coquena's messages are in Spanish in this version. Each section quotes the text
you will see.

## Port already in use

```text
Error: endpoint-unavailable. Puerto 9931 ocupado o escucha no disponible en 127.0.0.1
```

Another process is using the service port, or that address cannot be used. Stop
that process or give the service another port in its recipe:

```yaml
name: home-lab
service:
  backend:
    version: latest
    variant: cpu
  server:
    args:
      port: 19931
```

Point your client at the new port.

## A download fails

```text
Error: retries-exhausted. Preparación no completada; conserve el GUID para reanudar.
```

The download failed repeatedly, for example because of a temporary server error.
Run `coquena download list` to see the operation and its last error, then try again
later with `coquena download resume <download-id>`.

## `up` or `down` fails in a script

```text
Error: interaction-required. Up/down y apply/withdraw requieren interacción.
```

`up`, `down`, `apply`, and `withdraw` may need your confirmation, so they must run
in an interactive terminal. They exit with this error in scripts, CI jobs, or when
input is redirected. `--start-service` only authorizes starting the service; it does
not remove this requirement.

## A request returns "model not found"

```json
{"error":{"code":400,"message":"model 'ai-lab/chat' not found"}}
```

The preset is not published on that service. Run `coquena up` from the project and
check that `coquena ps` lists it. Use the full `recipe-name/preset` name, and make
sure the client points at the right port.

## `verify` reports an unavailable resource

```text
model chat-model — Recurso no disponible. Revisá la referencia en la receta y prepará la instalación.
```

The recipe references a resource that is not prepared in this scope. Run
`coquena up` to prepare it, or check the path if it is a local file.

## Inspect a service

Start with these commands. `logs` shows the recent `llama-server` output.

```powershell
coquena ps
coquena service default show
coquena logs --service default
```

## Report a problem

Open an issue on [GitHub](https://github.com/mstranich/coquena/issues). Check logs
and recipes for private paths or tokens before sharing them.
