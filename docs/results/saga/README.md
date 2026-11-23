# Результаты тестов

| Проверка | Результат | Отчёт |
|---|---|---|
| .NET | 78 успешно, 0 ошибок | [dotnet-tests.txt](dotnet-tests.txt) |
| Postman / Newman | 61 запрос, 155 проверок, 0 ошибок | [newman-summary.json](newman-summary.json) |
| Перезапуск участников и аварийное завершение Orders | Сага восстановилась, деньги и товар возвращены | [recovery-check.txt](recovery-check.txt) |
| Helm lint | 0 ошибок | [helm-lint.txt](helm-lint.txt) |
| Helm install | Release deployed в namespace blt | [helm-release.json](helm-release.json) |
| Kubernetes | Сервисы готовы, PVC Bound, ingress arch.homework | [cluster-check.txt](cluster-check.txt) |

```powershell
dotnet test Blt.slnx
./scripts/test-saga.ps1 -BaseUrl http://127.0.0.1:8099
./scripts/test-saga-recovery.ps1
```
