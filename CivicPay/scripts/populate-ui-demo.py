#!/usr/bin/env python3
"""Populate a fresh local seeded sandbox through real APIs for dashboard screenshots.

Never changes timestamps or manufactures telemetry. Refuses a database with existing
batches. Run against a disposable SQLite demo, not a client environment.
"""
import datetime, json, os, urllib.error, urllib.parse, urllib.request, uuid
from pathlib import Path
BASE = os.environ.get('CIVICPAY_URL', 'http://127.0.0.1:5080')
assert urllib.parse.urlparse(BASE).hostname in ('127.0.0.1', 'localhost', '::1'), 'Local sandbox only'
KEY = os.environ.get('CIVICPAY_API_KEY', '')
evidence = []

def request(path, body=None, expected=200, content_type='application/json', correlation=None):
    headers = {'X-Api-Key': KEY}
    if body is not None: headers['Content-Type'] = content_type
    if correlation: headers['X-Correlation-ID'] = correlation
    req = urllib.request.Request(BASE + path, data=body, headers=headers)
    try:
        with urllib.request.urlopen(req) as response: status, data = response.status, response.read()
    except urllib.error.HTTPError as error: status, data = error.code, error.read()
    result = json.loads(data)
    assert status == expected, (path, status, result)
    evidence.append({'path': path, 'httpStatus': status, 'response': result})
    return result

def upload(text, kind, name, expected=201):
    boundary = 'civicpay-' + uuid.uuid4().hex
    body = (f'--{boundary}\r\nContent-Disposition: form-data; name="kind"\r\n\r\n{kind}\r\n'
            f'--{boundary}\r\nContent-Disposition: form-data; name="file"; filename="{name}"\r\n'
            'Content-Type: text/csv\r\n\r\n' + text + f'\r\n--{boundary}--\r\n').encode()
    return request('/api/imports', body, expected, 'multipart/form-data; boundary=' + boundary)

assert request('/api/health')['synthetic']
initial = request('/api/monitoring')
assert not initial['imports'] and len(initial['municipalities']) == 3, 'Use a fresh, seeded sandbox database'
root = Path(__file__).resolve().parents[1]
accounts = upload((root/'data/valid/accounts.csv').read_text(), 'accounts', 'municipal-opening-balances.csv')
valid = upload((root/'data/valid/payments.csv').read_text(), 'payments', 'legacy-payments-approved.csv')
repeat = upload((root/'data/valid/payments.csv').read_text(), 'payments', 'legacy-payments-retry.csv')
invalid = upload((root/'data/invalid/payments.csv').read_text(), 'payments', 'legacy-payments-exceptions.csv')
assert (accounts['totalSourceAmount'], accounts['totalImportedAmount']) == (1575,1575)
assert valid['status'] == 'RECONCILED' and valid['difference'] == 0 and valid['totalImportedAmount'] == 250.50
assert repeat['skippedRecordCount'] == 3 and repeat['totalImportedAmount'] == 0 and repeat['difference'] == 250.50
assert invalid['rejectedRecordCount'] == 7 and not invalid['sourceAmountComplete']
# A larger account batch exercises the existing row pagination endpoint.
large = upload('municipalityCode,accountNumber,paymentType,balance\n' + ''.join(
    f'DEMO-COUNTY,ONBOARD-UT-{i:04},Utility,{175+i*5}.00\n' for i in range(1,57)), 'accounts', 'county-utility-opening-balances.csv')
assert large['importedRecordCount'] == 56
failed = upload('municipalityCode,accountNumber,paymentType,amount,transactionDate,externalReference\n'
    'PINE-VALLEY,MISSING-PK-804,ParkingTicket,75,2026-01-01,MIG-PV-804\n'
    'PINE-VALLEY,MISSING-PK-805,ParkingTicket,125,2026-01-01,MIG-PV-805\n', 'payments', 'pine-valley-unmapped-accounts.csv')
assert failed['status'] == 'FAILED' and failed['rejectedRecordCount'] == 2
upload('wrong,headers\n', 'payments', 'invalid-export-header.csv', 400)
today = datetime.datetime.now(datetime.timezone.utc).date().isoformat()
first_payload = None
for code in ['DEMO-COUNTY','RIVERBEND','PINE-VALLEY']:
    config = next(m for m in initial['municipalities'] if m['municipalityCode'] == code)
    eligible = [a for a in request('/api/monitoring/accounts?municipality='+code+'&pageSize=200')['items'] if a['balance'] > 0]
    for i, account in enumerate(eligible[:6]):
        payload = {'municipalityCode':code,'accountNumber':account['accountNumber'],'paymentType':account['paymentType'],
                   'amount':account['balance'] if not config['allowPartialPayments'] else [42.50,75,125.25,250,86.75,150][i],
                   'transactionDate':today,'externalReference':f'PORTAL-{code}-{today}-{i+1:03}'}
        accepted = request('/api/payments', json.dumps(payload).encode(), 201)
        if first_payload is None: first_payload, first_result = payload, accepted
# Exact retry succeeds, changed amount conflicts, neither duplicates accepted records.
replayed = request('/api/payments', json.dumps(first_payload).encode())
assert replayed['replayed'] and replayed['transactionId'] == first_result['transactionId']
request('/api/payments', json.dumps({**first_payload, 'amount':43}).encode(), 409, correlation='cp-demo-reference-conflict')
for i, (code, service) in enumerate([('PINE-VALLEY','ParkingTicket')]*4 + [('RIVERBEND','Utility')]*2):
    request('/api/payments',json.dumps({'municipalityCode':code,'accountNumber':f'LEGACY-UNMAPPED-{800+i}',
        'paymentType':service,'amount':75,'transactionDate':today,'externalReference':f'UNMAPPED-{i}'}).encode(),404,
        correlation=f'cp-demo-account-map-{i+1:03}')
for code in ['PINE-VALLEY','PINE-VALLEY','RIVERBEND']:
    request('/api/payments', json.dumps({'municipalityCode':code,'accountNumber':'LEGACY-PT-1001',
        'paymentType':'PropertyTax' if code=='RIVERBEND' else 'Utility','amount':25,'transactionDate':today,
        'externalReference':f'SERVICE-MAPPING-{uuid.uuid4().hex[:8]}'}).encode(),422)
for report in [accounts,valid,repeat,invalid,large,failed]:
    assert request('/api/imports/'+report['batchId']+'/reconciliation') == report
snapshot = request('/api/monitoring')
assert snapshot['metrics']['successfulTransactions'] == initial['metrics']['successfulTransactions'] + 21
assert snapshot['metrics']['failedTransactions'] == 10
assert 'paths' in request('/swagger/v1/swagger.json')
output = root/'docs/verification/ui-api-checks.json'
output.write_text(json.dumps({'baseUrl':BASE,'executedAt':datetime.datetime.now(datetime.timezone.utc).isoformat(),
    'checks':evidence,'result':'passed'},indent=2)+'\n')
print(json.dumps({'result':'passed','acceptedRecords':snapshot['metrics']['successfulTransactions'],
    'failedPaymentRequests':snapshot['metrics']['failedTransactions'],'batches':len(snapshot['imports']),
    'evidence':str(output)},indent=2))
