"""Generate editable SVG and Mermaid sequence sources for homework 8 (stdlib only)."""
from html import escape
from pathlib import Path

OUT = Path(__file__).resolve().parents[1] / 'docs' / 'streaming'
OUT.mkdir(parents=True, exist_ok=True)

def text(x, y, value, size=16, color='#1e293b', anchor='middle'):
    return f'<text x="{x}" y="{y}" font-size="{size}" fill="{color}" text-anchor="{anchor}">{escape(value)}</text>'

def start(width, height, title):
    return [f'<svg xmlns="http://www.w3.org/2000/svg" width="{width}" height="{height}" viewBox="0 0 {width} {height}" font-family="Arial, sans-serif">',
            '<defs><marker id="arrow" markerWidth="9" markerHeight="9" refX="8" refY="4" orient="auto"><path d="M0,0 L8,4 L0,8" fill="none" stroke="#334155" stroke-width="1.5"/></marker></defs>',
            f'<rect width="{width}" height="{height}" fill="#f8fafc"/>', text(44, 44, title, 26, anchor='start')]

def box(parts, x, y, w, h, title, subtitle='', fill='#dbeafe'):
    parts.append(f'<rect x="{x}" y="{y}" width="{w}" height="{h}" rx="12" fill="{fill}" stroke="#94a3b8"/>')
    parts.append(text(x+w/2, y+30, title, 18))
    if subtitle: parts.append(text(x+w/2, y+55, subtitle, 14, '#475569'))

def arrow(parts, x1, y1, x2, y2, label='', dashed=False, label_x=None, label_y=None):
    dash='stroke-dasharray="6 4"' if dashed else ''
    parts.append(f'<line x1="{x1}" y1="{y1}" x2="{x2}" y2="{y2}" stroke="#334155" stroke-width="1.7" {dash} marker-end="url(#arrow)"/>')
    if label: parts.append(text(label_x if label_x is not None else (x1+x2)/2, label_y if label_y is not None else (y1+y2)/2-9, label, 14))

def sequence(name, title, lanes, rows):
    width=1440; height=190+len(rows)*62
    parts=start(width,height,title)
    coords={key:110+i*(1220/(len(lanes)-1)) for i,(key,_) in enumerate(lanes)}
    mermaid=['sequenceDiagram']+[f'    participant {key} as {label}' for key,label in lanes]
    for key,label in lanes:
        x=coords[key]
        box(parts,x-84,70,168,55,label)
        parts.append(f'<line x1="{x}" y1="125" x2="{x}" y2="{height-35}" stroke="#94a3b8" stroke-dasharray="5 5"/>')
    for i,row in enumerate(rows):
        y=170+i*62
        if row[0]=='note':
            parts.append(f'<rect x="40" y="{y-28}" width="1360" height="42" rx="7" fill="#e2e8f0"/>')
            parts.append(text(width/2,y-1,row[1],16))
            mermaid.append(f'    Note over {lanes[0][0]},{lanes[-1][0]}: {row[1]}')
        elif row[0] in ('alt','else','end'):
            kind=row[0]; label=row[1] if len(row)>1 else ''
            parts.append(f'<rect x="40" y="{y-28}" width="1360" height="38" fill="'+('#dcfce7' if kind=='alt' else '#fee2e2' if kind=='else' else '#e2e8f0')+'"/>')
            parts.append(text(65,y-3,kind.upper()+(' — '+label if label else ''),15,anchor='start'))
            mermaid.append(f'    {kind} {label}'.rstrip())
        else:
            sender,receiver,label,*response=row
            x1,x2=coords[sender],coords[receiver]
            dashed=bool(response and response[0])
            if x1==x2:
                parts.append(f'<path d="M{x1},{y-18} h60 v25 h-60" fill="none" stroke="#334155" marker-end="url(#arrow)"/>')
                parts.append(text(x1+70,y-3,label,14,anchor='start'))
            else: arrow(parts,x1,y,x2,y,label,dashed)
            mermaid.append(f'    {sender}{"-->>" if dashed else "->>"}{receiver}: {label}')
    parts.append('</svg>')
    (OUT/f'{name}.svg').write_text('\n'.join(parts),encoding='utf-8')
    (OUT/f'{name}.mmd').write_text('\n'.join(mermaid)+'\n',encoding='utf-8')

sequence('http-only','1 · Только HTTP', [('C','Клиент'),('O','Orders'),('B','Billing'),('N','Notifications')], [
    ('note','Предусловие: пользователь зарегистрирован, счёт создан, деньги внесены'),
    ('C','O','POST /orders {price}'),('O','B','POST /internal/charges {orderId, userId, amount}'),
    ('alt','Средств достаточно'),('B','O','200 succeeded; баланс уменьшен',True),
    ('O','N','POST /internal/notifications: Письмо счастья'),('N','O','201: письмо сохранено в БД',True),
    ('else','Недостаточно средств'),('B','O','200 rejected; баланс прежний',True),
    ('O','N','POST /internal/notifications: Письмо горя'),('N','O','201: письмо сохранено в БД',True),
    ('end',),('O','C','201 {id, status: paid | rejected}',True),('C','N','GET /notifications'),('N','C','200: список писем',True)])

sequence('http-kafka','2 · HTTP + Kafka для уведомлений — реализованный вариант', [('C','Клиент'),('O','Orders'),('B','Billing'),('K','Kafka'),('N','Notifications')], [
    ('note','POST /auth/register → Users → Billing: создать счёт; затем пополнение через /billing/deposits'),
    ('C','O','POST /orders {price}'),('O','B','POST /internal/charges'),
    ('alt','Средств достаточно'),('B','O','200 succeeded; баланс уменьшен',True),
    ('else','Недостаточно средств'),('B','O','200 rejected; баланс прежний',True),('end',),
    ('note','Orders: одна транзакция PostgreSQL сохраняет paid/rejected и событие в outbox'),
    ('O','C','201 {id, status: paid | rejected}',True),('O','K','Outbox → OrderCompleted.v1 (key = orderId)'),
    ('K','N','OrderCompleted.v1'),('note','Notifications: сохранить Письмо счастья / Письмо горя; eventId и orderId UNIQUE'),
    ('N','K','Commit offset после записи в БД',True),('C','N','GET /notifications?orderId=... (poll)'),('N','C','200: сохранённое письмо',True)])

sequence('event-collaboration','3 · Event Collaboration', [('C','Клиент'),('O','Orders'),('K','Kafka'),('B','Billing'),('N','Notifications')], [
    ('note','Каждый сервис реагирует на факты; отдельного координатора процесса нет'),
    ('C','O','POST /orders {price}'),('O','C','202 {id, status: processing}',True),
    ('O','K','OrderCreated.v1 (outbox)'),('K','B','OrderCreated.v1'),
    ('alt','Средств достаточно'),('B','K','PaymentSucceeded.v1: списание + outbox'),
    ('else','Недостаточно средств'),('B','K','PaymentDeclined.v1: отказ + outbox'),('end',),
    ('K','O','PaymentSucceeded.v1 / PaymentDeclined.v1'),
    ('note','Orders: inbox + paid/rejected + outbox в одной транзакции'),
    ('O','K','OrderCompleted.v1'),('K','N','OrderCompleted.v1'),
    ('note','Notifications: письмо счастья / горя; commit offset после сохранения'),
    ('C','O','GET /orders/{id} (poll)'),('O','C','200 paid / rejected',True),
    ('C','N','GET /notifications?orderId=... (poll)'),('N','C','200: сохранённое письмо',True)])

p=start(1440,910,'BLT · Stream Processing · Apache Kafka')
box(p,535,78,370,75,'Postman / Newman','http://arch.homework', '#e0e7ff')
box(p,535,207,370,75,'Ingress','namespace: ingress-nginx', '#e0e7ff')
arrow(p,720,153,720,207,'HTTP')
services=[('Users',55),('Billing',395),('Orders',735),('Notifications',1075)]
for title,x in services:
    box(p,x,405,280,78,title,'.NET 10 · namespace: blt')
    box(p,x,733,280,70,title+' DB','PostgreSQL · собственная БД','#e2e8f0')
    if title == 'Orders':
        p.append('<path d="M875,483 V520 H1045 V700 H875 V733" fill="none" stroke="#334155" stroke-width="1.7" marker-end="url(#arrow)"/>')
    else:
        arrow(p,x+140,483,x+140,733,'',False)
p.append('<path d="M720,282 V325 H195 V405 M720,325 H1215 V405 M535,325 V405 M875,325 V405" fill="none" stroke="#334155" stroke-width="1.7"/>')
for x,label in [(195,'/auth, /profile'),(535,'/billing'),(875,'/orders'),(1215,'/notifications')]:p.append(text(x,364,label,16))
arrow(p,335,447,395,447,'',False);p.append(text(366,506,'Счёт',14))
arrow(p,735,447,675,447,'',False);p.append(text(704,506,'Списание',14))
box(p,620,570,400,88,'Apache Kafka · KRaft','blt.order.events.v1 · key = orderId','#ffedd5')
p.append('<path d="M1015,483 V540 H940 V570" fill="none" stroke="#334155" stroke-width="1.7" stroke-dasharray="6 4" marker-end="url(#arrow)"/>')
p.append(text(1090,539,'Outbox → событие',14))
p.append('<path d="M1020,614 H1135 V483" fill="none" stroke="#334155" stroke-width="1.7" stroke-dasharray="6 4" marker-end="url(#arrow)"/>')
p.append(text(1135,640,'OrderCompleted',14))
p.append(text(45,852,'Сплошная линия: HTTP / SQL. Пунктир: события. Каждое письмо сохраняется в БД; SMTP не используется.',17,anchor='start'))
p.append(text(45,880,'Bearer-сессии проверяются через Users /profile. Внутренние HTTP-вызовы защищены X-Service-Key.',16,anchor='start'))
p.append('</svg>')
(OUT/'architecture.svg').write_text('\n'.join(p),encoding='utf-8')
