# WonderFleet — common tasks. Run `make help` for the list.
.DEFAULT_GOAL := help
.PHONY: help db backend frontend build test up down migrate-check clean

help: ## Show the available targets
	@grep -E '^[a-z-]+:.*?## ' $(MAKEFILE_LIST) | awk 'BEGIN {FS = ":.*?## "}; {printf "  \033[36m%-14s\033[0m %s\n", $$1, $$2}'

db: ## Start PostgreSQL for local development
	docker compose -f deploy/docker-compose.yml up -d db

backend: ## Run the API (applies migrations and seeds the admin on first run)
	cd backend && dotnet run --project src/WonderFleet.Api

frontend: ## Run the web app against a local API
	cd frontend && npm run dev

build: ## Build both halves
	cd backend && dotnet build
	cd frontend && npm run build

test: ## Run the backend test suite
	cd backend && dotnet test

up: ## Run the whole stack in Docker
	docker compose -f deploy/docker-compose.yml up --build

down: ## Stop the Docker stack
	docker compose -f deploy/docker-compose.yml down

clean: ## Remove build output
	cd backend && dotnet clean
	rm -rf frontend/dist frontend/*.tsbuildinfo
