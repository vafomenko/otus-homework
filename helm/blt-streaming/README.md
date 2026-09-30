# BLT Stream Processing

```powershell
helm upgrade --install blt-streaming ./helm/blt-streaming --namespace blt --create-namespace --wait --wait-for-jobs --timeout 10m
```

Чарт устанавливает Users, Orders, Billing, Notifications, четыре PostgreSQL,
Apache Kafka в KRaft-режиме, PVC, миграции и топик `blt.order.events.v1`.
Namespace приложения — **blt**. Ingress `arch.homework` требует controller
с классом `nginx`; скрипт устанавливает его в **ingress-nginx**.

Образы приложения `vafomenko/blt-*-api:8.0.0` нужно собрать и загрузить на узлы
или в собственный registry. `scripts/setup-streaming.ps1` делает это для kind.
Нужен default StorageClass. Значения паролей в values предназначены для стенда.

[Архитектура, подготовка кластера и тесты](../../docs/streaming/README.md).
