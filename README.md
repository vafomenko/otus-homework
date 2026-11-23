# Домашнее задание №9 — Распределённые транзакции

Сервисы интернет-магазина на .NET 10, PostgreSQL и Kafka.
Создание заказа выполняется через **оркестрируемую Saga**:
оплата в Billing → резерв товара в Warehouse → резерв конкретного
курьера на временной слот в Delivery. При отказе выполняются компенсации.

Состояние и повторы сохраняются в БД; worker возобновляет
сагу после сбоя. [Сценарии перезапуска и действия при зависшем заказе](docs/saga/recovery.md).

- [Паттерн, архитектура, установка и сценарии проверки](docs/saga/README.md).
- [Helm-чарт](helm/blt-saga).
- [Postman-коллекция](postman/homework-9.postman_collection.json) и
  [окружение](postman/homework-9.postman_environment.json).
  Initial `{{baseUrl}}` в обоих файлах — **`http://arch.homework`**.
- [Результаты проверок](docs/results/saga/README.md).

Локальный стенд, PowerShell 7, Docker Desktop с Linux containers:

```powershell
./scripts/setup-saga.ps1
./scripts/test-saga.ps1 -BaseUrl http://127.0.0.1:8099
```

Скрипт собирает шесть образов и создаёт отдельный kind-кластер **`otus-hw9`**.
API, шесть отдельных PostgreSQL и Kafka устанавливаются в namespace **`blt`**;
ingress controller — в **`ingress-nginx`**. Kubeconfig: `.local/kubeconfig-saga`.
Коллекция передаёт `Host: arch.homework` при локальном переопределении URL.

Команды установки в подготовленный кластер с ingress, StorageClass и образами:

```powershell
kubectl create namespace blt --dry-run=client -o yaml | kubectl apply -f -
helm upgrade --install blt-saga ./helm/blt-saga --namespace blt --wait --wait-for-jobs --timeout 10m
```

Для созданного скриптом кластера укажите `--kubeconfig .local/kubeconfig-saga`
в командах kubectl и Helm. Полная подготовка описана в инструкции.

Автотесты используют временные PostgreSQL-контейнеры:

```powershell
dotnet test Blt.slnx
```
