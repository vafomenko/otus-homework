# Проверка ДЗ №8

Проверено **30.09.2026** в kind-кластере `otus-hw8`, namespace `blt`.
Четыре образа `8.0.0` собраны локально и загружены в kind.

| Проверка | Результат | Подтверждение |
|---|---|---|
| .NET integration tests, реальные временные PostgreSQL | 41 пройден, 0 ошибок | [dotnet-tests.txt](dotnet-tests.txt) |
| Newman через ingress | 29 запросов, 46 проверок, 0 ошибок | [newman-output.txt](newman-output.txt) |
| Helm lint | 0 ошибок | [helm-lint.txt](helm-lint.txt) |
| Установка/обновление Helm | deployed, revision 2 | [helm-install.txt](helm-install.txt) |
| Kubernetes server dry-run | Все ресурсы приняты | [manifests-check.txt](manifests-check.txt) |
| Четыре API, четыре БД и Kafka | Все 9 pod'ов Ready, 5 PVC Bound | [cluster-check.txt](cluster-check.txt) |
| Kafka consumer | 2 события обработаны; lag 0 в партиции с событиями | [kafka-consumer.txt](kafka-consumer.txt) |
| Содержимое Kafka | События paid и rejected с key = orderId | [kafka-events.txt](kafka-events.txt) |

Newman запущен командой:

```powershell
./scripts/test-streaming.ps1 -BaseUrl http://127.0.0.1:8098
```

Все запросы используют `{{baseUrl}}` и заголовок `Host: arch.homework`.
Initial/default URL коллекции и окружения — `http://arch.homework`.
В логе есть данные запросов и ответов, пароль и токен скрыты.

В [Billing](billing-database.txt) зафиксированы пополнение `10000`, успешное
списание `3000`, отказ на `8000` и отдельное снятие `500`. После успешного
заказа баланс `7000`; после отказа тоже `7000`; после дополнительной проверки
снятия — `6500`. Повтор снятия не создал второй операции. Счёт второго
пользователя создан автоматически и имеет баланс `0`.

В [Orders](orders-database.txt) два заказа: `paid` и `rejected`; обе записи
outbox опубликованы. В [Notifications](notifications-database.txt) по одному
письму счастья и горя для соответствующих заказов и отдельное тестовое
сообщение, созданное API. Реальные события Kafka дополнительно проверены
по JSON Schema из AsyncAPI, см. [contracts-check.txt](contracts-check.txt).

Автотесты дополнительно проверяют конкурентные списания одного счёта,
повторные пополнения/снятия/оплаты, сохранение отказа при повторе после
пополнения, недопустимые суммы, повторное создание счёта, дедупликацию
уведомлений и запись результата заказа вместе с outbox. Newman также
проверяет отсутствие анонимного доступа и изоляцию двух пользователей.

Логи содержат идентификаторы и email синтетических пользователей одного
запуска. Следующий запуск создаёт новых пользователей, поэтому ID и число
накопленных строк изменятся.
