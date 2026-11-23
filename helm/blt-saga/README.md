# blt-saga

Чарт устанавливает Users, Orders, Billing, Warehouse, Delivery, Notifications,
отдельную PostgreSQL для каждого сервиса и Kafka.

```powershell
kubectl create namespace blt --dry-run=client -o yaml | kubectl apply -f -
helm upgrade --install blt-saga ./helm/blt-saga --namespace blt --wait --wait-for-jobs --timeout 10m
```

Образы `10.0.0` должны быть собраны и доступны узлам. Чарт требует ingress
controller класса `nginx` и default StorageClass. Домен ingress — `arch.homework`.
Команды подготовки локального кластера и запуска тестов приведены в [инструкции к ДЗ №10](../../README.md).
Архитектура и компенсации описаны в [ДЗ №9](../../docs/saga/README.md).
