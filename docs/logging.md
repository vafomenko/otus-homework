# Централизованное логирование — ДЗ № 6

## Состав и ресурсы

Приложение и PostgreSQL устанавливаются в `blt`, EFK — в `logging`.
Elasticsearch/Kibana: `8.19.3`, Fluent Bit: `4.0.9`, bootstrap curl: `8.16.0`.
Это локальный учебный стенд: Elasticsearch single-node, heap 512 MiB,
PVC 5 GiB, один shard и ноль replicas у ежедневных индексов. Нужен default
StorageClass с динамическим provisioning; kind и Minikube предоставляют его.
Для полного стенда с предыдущим мониторингом выделите Docker/Minikube около
8 GiB RAM и 4 CPU. `node.store.allow_mmap=false` позволяет запускать ES без
изменения `vm.max_map_count` на машине.

В учебном Elasticsearch отключены authentication/TLS; это **не** настройка
безопасности. ES доступен только через ClusterIP, Kibana — NodePort 30561.
Используйте локальный кластер и loopback port-forward. Для общего кластера
нужны TLS, учётные записи и ограничение сетевого доступа.

Путь данных: stdout JSON → CRI/Docker-файлы ноды → tail input Fluent Bit →
kubernetes filter → parser filter → Elasticsearch → Kibana. Sidecar у API нет.
DaemonSet имеет tolerations для control-plane и собирает все namespaces,
кроме `logging`, чтобы служебные сообщения самого сборщика не создавали цикл.
`/var/log` и Docker-каталог смонтированы только для чтения. Docker socket
не нужен. Tail DB и буфер на hostPath переживают перезапуск Pod сборщика.
`Refresh_Interval 5` ускоряет обнаружение коротких Job.
`Generate_ID On` вместе с `Write_Operation index` позволяет безопасно повторять
отправку после сетевого сбоя без конфликтов create. Буфер ограничен 1 GiB; длительный отказ ES сверх его ёмкости может привести
к потере самых старых chunks. Ретенция индексов в этой учебной работе не настроена.

`Logstash_Format On` формирует `fluent-bit-logs-YYYY.MM.DD`.
В Elasticsearch 8 mapping types удалены, поэтому вместо передачи `Type _doc`
используется `Suppress_Type_Name On` — иначе bulk-запросы завершатся ошибкой.
См. [официальную документацию Fluent Bit](https://docs.fluentbit.io/manual/data-pipeline/outputs/elasticsearch).

Bootstrap Job устанавливает [index template](../logging/config/index-template.json)
до запуска Fluent Bit: `level`, `service`, `event` — keyword, `user_id` — long,
`@timestamp` — date. `kubernetes.labels` — flattened, чтобы новые лейблы
не увеличивали количество полей mapping. После изменения template удалите
только завершённый `job/elasticsearch-bootstrap` перед повторным `apply -k`;
новый template применяется к будущим индексам.

## Существующий kind из ДЗ № 5

Все команды из корня репозитория. Нужны PowerShell 7, Docker, kubectl,
.NET 10, Python 3; скрипт использует kind и Helm из PATH или `.tools` предыдущей работы.

```powershell
./scripts/setup-logging.ps1
```

Скрипт использует только `.local/kubeconfig`, собирает `6.0.0`, загружает образ
в `otus-hw5`, создаёт namespaces, ждёт EFK, запускает PostgreSQL и Helm с Job
миграций. Namespace создаётся `kubectl apply` из `logging/namespace.yaml` и
`infra/postgresql.yaml`; Helm также получает `--create-namespace` для `blt`.
Для создания исходного kind-стенда из ДЗ № 5: `./scripts/setup.ps1`.

## Новый Minikube: установка EFK и приложения

```powershell
minikube start -p otus-hw6 --cpus=4 --memory=8192
kubectl config use-context otus-hw6
kubectl create namespace blt --dry-run=client -o yaml | kubectl apply -f -
kubectl create namespace logging --dry-run=client -o yaml | kubectl apply -f -
kubectl get storageclass

docker build -t vafomenko/blt-users-api:6.0.0 .
minikube -p otus-hw6 image load vafomenko/blt-users-api:6.0.0

kubectl apply -k logging
kubectl -n logging rollout status statefulset/elasticsearch --timeout=600s
kubectl -n logging wait --for=condition=complete job/elasticsearch-bootstrap --timeout=600s
kubectl -n logging rollout status daemonset/fluent-bit --timeout=300s
kubectl -n logging rollout status deployment/kibana --timeout=600s

kubectl apply -f infra/postgresql.yaml
kubectl -n blt rollout status statefulset/users-db --timeout=300s
helm upgrade --install blt-users-api ./helm/blt-users-api -n blt --create-namespace `
  -f infra/homework-values.yaml --set image.tag=6.0.0 --set ingress.enabled=false --wait --timeout=5m
```

Файл `infra/homework-values.yaml` включает только учебную инъекцию HTTP 500
через `X-Demo-Error: 500`; по умолчанию она выключена. Команды ниже используют
port-forward и не требуют ingress. Если нужен ingress, включите addon
`minikube -p otus-hw6 addons enable ingress` и уберите `--set ingress.enabled=false`.

Вариант без Helm: после готовности БД и Fluent Bit примените
`k8s/00-namespace.yaml`, `k8s/01-configmap.yaml`, `k8s/02-secret.yaml`, затем
`k8s/03-migration-job.yaml`, дождитесь Job и примените `k8s/04-deployment.yaml`
и `k8s/05-service.yaml`. На повторном запуске предварительно удалите только
`job/blt-users-api-migrations` в `blt`. Образы raw-манифестов также `6.0.0`.
Для демонстрационного теста HTTP 500 дополнительно выполните:

```powershell
kubectl -n blt set env deployment/blt-users-api Observability__EnableFaultInjection=true
kubectl -n blt rollout status deployment/blt-users-api
```

## Проверка логов и открытие Kibana

В отдельных терминалах (для kind добавьте `--kubeconfig .local/kubeconfig`
ко всем командам kubectl):

```powershell
kubectl -n logging port-forward svc/kibana-service 5601:5601
kubectl -n logging port-forward svc/elasticsearch 9200:9200
kubectl -n blt port-forward svc/blt-users-api 8000:8000
```

Kibana: <http://localhost:5601>. Альтернатива NodePort:
`minikube -p otus-hw6 service kibana-service -n logging`.

```powershell
./scripts/test-logging.ps1
# CRUD, невалидный JSON/поля, конфликт, 404 и контролируемый 500;
# статусы проверяются, созданный тестовый пользователь удаляется.
python scripts/configure-kibana.py
```

Для проверки штатной остановки выполните `kubectl -n blt rollout restart
deployment/blt-users-api` и дождитесь rollout. Fluent Bit должен собрать
`application_stopping` и `application_stopped` из старых Pod, а
`application_started` — из новых. После рестарта возобновите API port-forward,
если выбранный им Pod был удалён.

Helm Job хранится сутки (`ttlSecondsAfterFinished: 86400`), а не удаляется
сразу после успеха. Следующий upgrade заменяет предыдущий Job. Логи в ES
остаются после удаления Job. Проверка:

```powershell
kubectl -n blt logs job/blt-users-api-migrations
kubectl -n logging logs daemonset/fluent-bit --tail=30
python scripts/verify-logging.py
```

Для воспроизводимого реального отказа БД есть `scripts/test-database-error.ps1`:
он создаёт отдельный временный Pod того же API с недоступным адресом БД,
проверяет HTTP 503 и удаляет Pod в `finally`. Основная БД продолжает работать.
По умолчанию он использует `.local/kubeconfig` учебного kind. Для Minikube
передайте `-Kubeconfig` с путём к kubeconfig этого кластера.

## Kibana: Data Views, Discover и визуализация

В версии 8 раздел **Stack Management → Data Views** соответствует
**Index Patterns** из задания. См. [документацию Kibana](https://www.elastic.co/guide/en/kibana/8.19/data-views.html).
Вручную: **Create data view**, имя/паттерн `fluent-bit-logs-*`, Timestamp field
`@timestamp`, **Save data view to Kibana**. Либо импортируйте подготовленные
объекты командой `python scripts/configure-kibana.py`; она также делает view
основным. Повторный импорт обновляет только объекты этой работы с фиксированными ID.

Откройте **Discover**, выберите view, период **Last 1 hour**. Добавьте колонки
`level`, `event`, `user_id`, `message`, `kubernetes.pod_name` и при необходимости
`request_id`, `method`, `path`, `ip`.

| Что найти | KQL |
|---|---|
| Все события API | `service: "user-service"` |
| Ошибки/предупреждения | `service: "user-service" and level: (ERROR or WARN)` |
| Конкретный пользователь | `service: "user-service" and user_id: 42` |
| Создание пользователя | `event: user_created and user_id: 42` |
| Job миграций | `service: "user-service" and event: migration_*` |
| Конкретные Pod | `kubernetes.pod_name: blt-users-api-*` |
| Один HTTP-запрос со всеми событиями | `request_id: "идентификатор-из-лога"` |
| Сбой БД | `event: database_error` |

Вместо 42 возьмите `user_id` из `docs/results/logging/http-checks.json`.
У Fluent Bit поле Pod называется `kubernetes.pod_name`, namespace —
`kubernetes.namespace_name`. `ip` — адрес TCP-клиента из HttpContext; при
запросе через ingress им может быть адрес прокси. Непроверенный
`X-Forwarded-For` не используется. Тела запросов, пароли, email и телефоны
специально не добавляются в события приложения. Ошибки сохраняют тип и stack
trace без текста exception, в котором драйвер БД может раскрыть значения.

Откройте **Dashboard → User service: centralized logs**: сохранены
гистограмма количества логов с разбивкой по `level` и таблица событий, период
последний час. Вручную аналог строится в Lens: X = date histogram по
`@timestamp`, Y = Count, breakdown = Top values of `level`.

## Скриншоты

```powershell
npm install --prefix .tools/browser playwright@1.55.0
node scripts/capture-kibana.cjs
```

Нужен установленный Google Chrome и открытый port-forward Kibana. Скрипт
снимает реальный UI без изменения изображений: Data Views, Discover с
разными уровнями, фильтр WARN/ERROR, Job миграций, поиск пользователя,
гистограмму. Результаты: [скриншоты](screenshots/logging/README.md).

## Диагностика

- `ImagePullBackOff`: проверьте доступ Docker/ноды к registry. Можно выполнить
  `docker pull` для образов из `logging/*.yaml`, затем `kind load docker-image
  ... --name otus-hw5` или `minikube -p otus-hw6 image load ...`.
- Pending PVC: `kubectl -n logging describe pvc data-elasticsearch-0`,
  проверьте default StorageClass и свободное место.
- Нет данных: дождитесь bootstrap Job, проверьте Fluent Bit на ошибки разбора,
  bulk и RBAC; затем `GET /fluent-bit-logs-*/_count` в Dev Tools Kibana.
- В Discover пусто: проверьте период, view и запрос `service: "user-service"`.
  Старые логи ДЗ № 5 были текстовыми и не имеют этих JSON-полей.

Справочный [репозиторий задания](https://github.com/MSA-SA-OTUS/logging-homework).
