SELECT status, step, COUNT(*) AS orders FROM orders
WHERE step <> 'legacy' GROUP BY status, step ORDER BY status, step;

SELECT id, status, step, reason, created_at, updated_at, deadline_at,
       recovery_attempts, retry_count, last_attempt_at, next_attempt_at, last_error
FROM orders WHERE status IN ('processing', 'compensating')
ORDER BY next_attempt_at, id LIMIT 50;

SELECT pid, application_name, state, NOW()-state_change AS since_last_query,
       wait_event_type, wait_event
FROM pg_stat_activity WHERE datname=current_database() AND application_name='blt-orders-saga';
