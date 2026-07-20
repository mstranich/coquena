package webui

import (
	"context"
	"embed"
	"errors"
	"flag"
	"fmt"
	"io"
	"mime"
	"net"
	"net/http"
	"path"
	"strings"
)

//go:embed dist/*
var assets embed.FS

const defaultPort = 8080

func ParsePort(args []string) (int, error) {
	flags := flag.NewFlagSet("webui", flag.ContinueOnError)
	flags.SetOutput(io.Discard)
	port := flags.Int("port", defaultPort, "HTTP port")
	if err := flags.Parse(args); err != nil || flags.NArg() != 0 || *port < 0 || *port > 65535 {
		return 0, errors.New("usage: coquena webui [--port <port>]")
	}
	return *port, nil
}

func Serve(ctx context.Context, port int, stdout io.Writer) error {
	if _, err := assets.ReadFile("dist/browser/index.html"); err != nil {
		return fmt.Errorf("read embedded Web UI: %w", err)
	}
	listener, err := net.Listen("tcp", net.JoinHostPort("127.0.0.1", fmt.Sprint(port)))
	if err != nil {
		return fmt.Errorf("listen on 127.0.0.1:%d: %w", port, err)
	}
	defer listener.Close()
	if _, err := fmt.Fprintf(stdout, "Coquena Web UI listening on http://%s\n", listener.Addr()); err != nil {
		return err
	}
	server := &http.Server{Handler: Handler()}
	go func() {
		<-ctx.Done()
		_ = server.Shutdown(context.Background())
	}()
	if err := server.Serve(listener); err != nil && !errors.Is(err, http.ErrServerClosed) {
		return err
	}
	return nil
}

func Handler() http.Handler {
	return http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		if r.URL.Path == "/api/health" {
			if r.Method != http.MethodGet {
				w.WriteHeader(http.StatusMethodNotAllowed)
				return
			}
			w.Header().Set("Content-Type", "text/plain; charset=utf-8")
			_, _ = io.WriteString(w, "ok\n")
			return
		}
		serveStatic(w, r)
	})
}

func serveStatic(w http.ResponseWriter, r *http.Request) {
	rawName := strings.TrimPrefix(r.URL.Path, "/")
	if rawName == ".." || strings.HasPrefix(rawName, "../") || strings.Contains(rawName, "/../") {
		http.NotFound(w, r)
		return
	}
	name := strings.TrimPrefix(path.Clean(r.URL.Path), "/")
	if name == ".." || strings.HasPrefix(name, "../") {
		http.NotFound(w, r)
		return
	}
	if name == "" || name == "." {
		name = "index.html"
	}
	data, err := assets.ReadFile("dist/browser/" + name)
	if err != nil {
		if path.Ext(name) != "" {
			http.NotFound(w, r)
			return
		}
		data, err = assets.ReadFile("dist/browser/index.html")
		if err != nil {
			http.Error(w, "embedded Web UI is unavailable", http.StatusInternalServerError)
			return
		}
		name = "index.html"
	}
	if name == "index.html" {
		w.Header().Set("Content-Type", "text/html; charset=utf-8")
	} else if contentType := mime.TypeByExtension(path.Ext(name)); contentType != "" {
		w.Header().Set("Content-Type", contentType)
	}
	_, _ = w.Write(data)
}
