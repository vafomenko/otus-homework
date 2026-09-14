# Домашнее задание № 6 — централизованное логирование EFK

Продолжение [CRUD из ДЗ № 4](docs/homework-4.md) и
[мониторинга из ДЗ № 5](docs/homework-5.md). .NET 10 пишет JSON в stdout,
Fluent Bit собирает контейнерные логи со всех нод Kubernetes, Elasticsearch
хранит их, Kibana позволяет искать события и строить графики.

## Материалы для сдачи

- [Установка, namespaces, проверка и запросы в Kibana](docs/logging.md).
- [EFK-манифесты и ConfigMap](logging/kustomization.yaml): Elasticsearch StatefulSet + PVC,
  headless/ClusterIP Service, Kibana Deployment + NodePort, Fluent Bit DaemonSet + RBAC.
- [Helm приложения](helm/blt-users-api) и [обычные манифесты](k8s).
- [Data view, Discover-запросы и дашборд](logging/kibana/saved-objects.ndjson).
- [Скриншоты Kibana](docs/screenshots/logging/README.md).
- [Результаты проверки](docs/results/logging/README.md).

## Запуск в существующем kind-кластере предыдущей работы

Из корня репозитория, PowerShell 7, Docker Desktop запущен:

```powershell
# Создаёт logging и blt; EFK запускается до миграций приложения.
./scripts/setup-logging.ps1
kubectl --kubeconfig .local/kubeconfig -n logging port-forward svc/kibana-service 5601:5601
```

Kibana: <http://localhost:5601>. В отдельном терминале:

```powershell
python scripts/configure-kibana.py
kubectl --kubeconfig .local/kubeconfig -n blt port-forward svc/blt-users-api 8000:8000
# Ещё в одном терминале:
./scripts/test-logging.ps1
```

Для нового Minikube, установки из обычных манифестов и генерации скриншотов
см. [полную инструкцию](docs/logging.md). Образ `6.0.0` собирается локально;
его нужно загрузить в kind/Minikube, публикация в Docker Hub не требуется.

| События | Уровень | Поля для поиска |
|---|---|---|
| Старт и штатная остановка | INFO | `event: application_*` |
| HTTP-запрос и ответ | INFO | `method`, `path`, `ip`, `request_id`, `status_code` |
| CRUD | INFO | `event: user_created/user_read/user_updated/user_deleted`, `user_id` |
| Валидация, конфликт, отсутствующий пользователь | WARN | `event`, `reason`, `user_id` |
| Сбой БД или приложения | ERROR | `event: database_error/application_error/http_error`, `exception_type`, `stack_trace` |
| Запуск/окончание миграций | INFO | `event: migration_started/migration_completed`, `kubernetes.pod_name` |

В Kibana 8 раздел **Index Patterns** называется **Data Views**. Используется
`fluent-bit-logs-*` с полем времени `@timestamp`.
