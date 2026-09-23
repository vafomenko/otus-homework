# Helm-чарт приложения

Для ДЗ № 7 стенд поднимается через [scripts/setup-auth.ps1](../../scripts/setup-auth.ps1).
Образ — `vafomenko/blt-users-api:7.0.0`, метрики доступны на `/metrics`.
`observability.enableFaultInjection=false` по умолчанию. Numeric UID 1654
позволяет Kubernetes проверить `runAsNonRoot` до запуска контейнера.

Установка в namespace `blt`:

```powershell
helm upgrade --install blt-users-api ./helm/blt-users-api --namespace blt --create-namespace --wait --timeout 5m
```

Для общего кластера вынесите `Database__Username` и `Database__Password` в
заранее созданный Secret и задайте его имя через `database.existingSecret` в
отдельном values-файле. Job миграции выполняется как `pre-install`/`pre-upgrade`
hook и завершается до создания обычного трафика релиза.

JSON-логирование и EFK: [инструкция ДЗ № 6](../../docs/logging.md).
Успешный Job миграций хранится сутки для сбора логов.

Регистрация, отзыв сессий, TLS и проверка через Newman:
[инструкция ДЗ № 7](../../docs/authentication.md).
`session.lifetimeMinutes=60` задаёт срок сессии. Профили и сессии общие
для реплик через PostgreSQL; пароль пользователя и bearer-токен не хранятся открыто.
