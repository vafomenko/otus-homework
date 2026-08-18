# Helm-чарт приложения

Установка в namespace `blt`:

```powershell
helm upgrade --install blt-orders-api ./helm/blt-orders-api --namespace blt --create-namespace
```

Для общего кластера вынесите `Database__Username` и `Database__Password` в
заранее созданный Secret и задайте его имя через `database.existingSecret` в
отдельном values-файле. Job миграции выполняется как `pre-install`/`pre-upgrade`
hook и завершается до создания обычного трафика релиза.
