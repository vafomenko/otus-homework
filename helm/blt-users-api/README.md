# Helm-чарт приложения

Для ДЗ № 5 полный стенд поднимается через [scripts/setup.ps1](../../scripts/setup.ps1).
Образ - `vafomenko/blt-users-api:6.0.0`, метрики доступны на `/metrics`.
`observability.enableFaultInjection=false` по умолчанию. Numeric UID 1654
позволяет Kubernetes проверить `runAsNonRoot` до запуска контейнера.

Установка в namespace `blt`:

```powershell
helm upgrade --install blt-users-api ./helm/blt-users-api --namespace blt --create-namespace
```

Для общего кластера вынесите `Database__Username` и `Database__Password` в
заранее созданный Secret и задайте его имя через `database.existingSecret` в
отдельном values-файле. Job миграции выполняется как `pre-install`/`pre-upgrade`
hook и завершается до создания обычного трафика релиза.

JSON-логирование и EFK: [инструкция ДЗ № 6](../../docs/logging.md).
Успешный Job миграций хранится сутки для сбора логов.
