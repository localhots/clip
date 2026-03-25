.PHONY: help build test check
.PHONY: fmt fmt-cs fmt-py
.PHONY: bench bench-full bench-clip bench-clip-full bench-asm bench-update archive-bench
.PHONY: charts docs pdf usage
.PHONY: demo demo-console demo-json
.PHONY: pkg
.PHONY: setup clean

BENCH_PROJECT := Clip.Benchmarks

## Show all targets with descriptions
help:
	@awk '/^## /{d=substr($$0,4)} /^[a-zA-Z_-]+:/ && d{n=$$1; sub(/:$$/,"",n); \
		printf "  \033[36m%-16s\033[0m %s\n",n,d; d=""}' $(MAKEFILE_LIST) | sort

## Build all projects in Release mode
build:
	dotnet build -c Release

## Run unit tests
test:
	@dotnet test Clip.slnx -c Release

## Build and test (quick validation)
check: build test

## Format all code (C# + Python)
format: format-cs format-py

## Format C# code
format-cs:
	dotnet format Clip.slnx

## Format Python scripts
format-py:
	uv run ruff format scripts/

## Run fast benchmarks — all loggers (~40 min)
bench:
	BENCH_MODE=fast dotnet run -c Release --project $(BENCH_PROJECT) -- --filter '*ConsoleBenchmarks*' '*JsonBenchmarks*' '*FilteredBenchmarks*'
	@rm -f tmp/BenchmarkDotNet.Artifacts/*.log
	@$(MAKE) bench-update

## Run full benchmarks — all loggers (~90 min, publication-quality)
bench-full:
	BENCH_MODE=full dotnet run -c Release --project $(BENCH_PROJECT) -- --filter '*ConsoleBenchmarks*' '*JsonBenchmarks*' '*FilteredBenchmarks*'
	@rm -f tmp/BenchmarkDotNet.Artifacts/*.log
	@$(MAKE) bench-update

## Run fast benchmarks — Clip only (~5 min)
bench-clip:
	BENCH_MODE=fast dotnet run -c Release --project $(BENCH_PROJECT) -- --filter '*_Clip' '*_ClipZero' '*_ClipMEL'
	@rm -f tmp/BenchmarkDotNet.Artifacts/*.log
	@$(MAKE) bench-update

## Run full benchmarks — Clip only
bench-clip-full:
	BENCH_MODE=full dotnet run -c Release --project $(BENCH_PROJECT) -- --filter '*_Clip' '*_ClipZero' '*_ClipMEL'
	@rm -f tmp/BenchmarkDotNet.Artifacts/*.log
	@$(MAKE) bench-update

## Import results into DB and archive raw artifacts
bench-update:
	@uv run python3 scripts/benchdb.py import
	@$(MAKE) archive-bench

## Dump JIT assembly for Clip hot paths
bench-asm:
	BENCH_CONFIG=asm dotnet run -c Release --project $(BENCH_PROJECT) -- --filter '*FiveFields_Clip*'
	BENCH_CONFIG=asm dotnet run -c Release --project $(BENCH_PROJECT) -- --filter '*WithContext_Clip*'
	@rm -f tmp/BenchmarkDotNet.AsmArtifacts/*.log

## Archive benchmark results to tmp/bench-history/
archive-bench:
	@sha=$$(git rev-parse --short=7 HEAD); \
	dirty=$$(git diff --quiet && echo "" || echo "-dirty"); \
	ts=$$(date +%Y%m%dT%H%M%S); \
	dir="tmp/bench-history/$${ts}_$${sha}$${dirty}"; \
	mkdir -p "$$dir"; \
	cp tmp/BenchmarkDotNet.Artifacts/results/*.md tmp/BenchmarkDotNet.Artifacts/results/*.csv "$$dir/" 2>/dev/null; \
	echo "Archived to $$dir"

## Generate bar charts from benchmark database
charts:
	uv run python3 scripts/chart.py

## Generate docs/COMPARE.md (depends on charts)
docs: charts
	uv run python3 scripts/compare.py

## Generate PDFs from docs
pdf: docs
	uv run python3 scripts/pdf.py

## Generate docs/USAGE.md (code + output for all loggers)
usage:
	dotnet run -c Release --project Clip.ComparisonDemo > tmp/raw/usage.txt
	uv run python3 scripts/usage.py tmp/raw/usage.txt

## Run the demo app (console output)
demo: demo-console

## Run the demo app (console output)
demo-console:
	dotnet run -c Release --project Clip.Demo

## Run the demo app (JSON output)
demo-json:
	dotnet run -c Release --project Clip.Demo -- json

## Pack NuGet packages into pkg/
pkg:
	@rm -rf pkg
	@mkdir -p pkg
	dotnet pack Clip/Clip.csproj -c Release -o pkg --nologo -v q
	dotnet pack Clip.OpenTelemetry/Clip.OpenTelemetry.csproj -c Release -o pkg --nologo -v q
	dotnet pack Clip.Extensions.Logging/Clip.Extensions.Logging.csproj -c Release -o pkg --nologo -v q
	dotnet pack Clip.Analyzers/Clip.Analyzers.csproj -c Release -o pkg --nologo -v q
	@echo ""
	@ls -lh pkg/*.nupkg

## Install Python dependencies
setup:
	uv sync

## Remove build artifacts and benchmark results
clean:
	dotnet clean -c Release --nologo -v q
	rm -rf tmp/BenchmarkDotNet.Artifacts tmp/BenchmarkDotNet.AsmArtifacts tmp/charts
