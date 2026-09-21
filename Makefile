# WonderFleet — common tasks. Run `make help` for the list.
.DEFAULT_GOAL := help
.PHONY: help env db db-status backend frontend build test up down logs clean

help: ## Show the available targets
	@grep -E '^[a-z-]+:.*?## ' $(MAKEFILE_LIST) | awk 'BEGIN {FS = ":.*?## "}; {printf "  \033[36m%-14s\033[0m %s\n", $$1, $$2}'

env: ## Create .env from the template (does not overwrite an existing one)
	@test -f .env || cp deploy/.env.example .env
	@echo ".env ready"

db: ## Start PostgreSQL for local development (host port 5433)
	docker compose -f deploy/docker-compose.yml up -d db

db-status: ## Show whether the database container is healthy
	docker compose -f deploy/docker-compose.yml ps

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

logs: ## Tail the Docker stack logs
	docker compose -f deploy/docker-compose.yml logs -f --tail=100

clean: ## Remove build output
	cd backend && dotnet clean
	rm -rf frontend/dist frontend/*.tsbuildinfo
