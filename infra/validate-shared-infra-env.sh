#!/usr/bin/env bash
set -euo pipefail

required_keys=(
  AZURE_OPENAI_ENDPOINT
  AZURE_OPENAI_API_KEY
  AZURE_OPENAI_CHAT_DEPLOYMENT
  AZURE_OPENAI_EMBEDDING_DEPLOYMENT
  AZURE_AI_SEARCH_ENDPOINT
  AZURE_AI_SEARCH_API_KEY
  AZURE_BLOB_CONNECTION_STRING
  AZURE_DOCUMENT_INTELLIGENCE_ENDPOINT
  AZURE_DOCUMENT_INTELLIGENCE_KEY
)

declare -A env_values
while IFS='=' read -r key value; do
  value="${value%\"}"
  value="${value#\"}"
  env_values["$key"]="$value"
done < <(azd env get-values)

missing=()
for key in "${required_keys[@]}"; do
  if [[ -z "${env_values[$key]:-}" ]]; then
    missing+=("$key")
  fi
done

if (( ${#missing[@]} > 0 )); then
  echo
  echo 'Shared infrastructure environment validation failed.' >&2
  echo 'azd is configured to deploy only the app layer in this repo.' >&2
  echo 'Populate the shared infrastructure and Foundry/OpenAI-compatible settings in the active azd environment before running azd provision or azd deploy.' >&2
  echo >&2
  echo 'Missing required azd env values:' >&2
  for key in "${missing[@]}"; do
    echo "  - $key" >&2
  done
  echo >&2
  exit 1
fi

echo 'Shared infrastructure environment validation passed.'