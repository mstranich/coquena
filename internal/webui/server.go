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

	"github.com/mstranich/coquena/internal/installation"
	hostsystem "github.com/mstranich/coquena/internal/system"
)

//go:embed dist/*
var assets embed.FS

const defaultPort = 8080

type inspectFunc func() (hostsystem.Report, error)
type ConfigService interface {
	ModelsDirectory() (string, error)
	SetModelsDirectory(string) (string, error)
}
type installationConfigService struct{}

func (installationConfigService) ModelsDirectory() (string, error) {
	return installation.ModelsDirectory()
}
func (installationConfigService) SetModelsDirectory(v string) (string, error) {
	return installation.SetModelsDirectory(v)
}
func (installationConfigService) RuntimesDirectory() (string, error) {
	return installation.RuntimesDirectory()
}
func (installationConfigService) SetRuntimesDirectory(v string) (string, error) {
	return installation.SetRuntimesDirectory(v)
}

func ParsePort(args []string) (int, error) {
	flags := flag.NewFlagSet("webui", flag.ContinueOnError)
	flags.SetOutput(io.Discard)
	port := flags.Int("port", defaultPort, "HTTP port")
	if err := flags.Parse(args); err != nil || flags.NArg() != 0 || *port < 0 || *port > 65535 {
		return 0, errors.New("usage: coquena webui [--port <port>]")
	}
	return *port, nil
}

func Serve(ctx context.Context, port int, stdout, stderr io.Writer, runtimes ...RuntimeService) error {
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
	server := &http.Server{Handler: HandlerWithServicesAndLogger(hostsystem.Inspect, firstRuntime(runtimes), stderr)}
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
	return HandlerWithInspector(hostsystem.Inspect)
}

func HandlerWithInspector(inspect inspectFunc) http.Handler {
	return HandlerWithServices(inspect, nil)
}

func firstRuntime(services []RuntimeService) RuntimeService {
	if len(services) == 0 {
		return nil
	}
	return services[0]
}

func HandlerWithServices(inspect inspectFunc, runtimes RuntimeService) http.Handler {
	return HandlerWithServicesAndLogger(inspect, runtimes, io.Discard)
}

func HandlerWithServicesAndLogger(inspect inspectFunc, runtimes RuntimeService, logs io.Writer) http.Handler {
	runtimeAPI := newRuntimeAPI(runtimes, logs)
	configAPI := newConfigAPI(installationConfigService{})
	modelAPI := newModelAPI(logs)
	fieldsAPI := newFieldsAPI()
	presetsAPI := newPresetsAPI()
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
		if r.URL.Path == "/api/system" {
			if r.Method != http.MethodGet {
				w.WriteHeader(http.StatusMethodNotAllowed)
				return
			}
			report, err := inspect()
			if err != nil {
				http.Error(w, `{"error":"system inspection failed"}`, http.StatusInternalServerError)
				return
			}
			w.Header().Set("Content-Type", "application/json; charset=utf-8")
			if err := hostsystem.WriteJSON(w, report); err != nil {
				return
			}
			return
		}
		if runtimeAPI.serve(w, r) {
			return
		}
		if modelAPI.serve(w, r) {
			return
		}
		if fieldsAPI.serve(w, r) {
			return
		}
		if presetsAPI.serve(w, r) {
			return
		}
		if configAPI.serve(w, r) {
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
