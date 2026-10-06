#!/usr/bin/env python3
"""Execute a repeatable HTTP audit against a running, synthetic CivicPay instance.

Optionally set CIVICPAY_AUDIT_DATABASE to its SQLite file for independent
balance, transaction and reconciliation checks. Never use real customer data.
"""
import concurrent.futures
import datetime
from decimal import Decimal
import json
import os
from pathlib import Path
import sqlite3
import urllib.error
import urllib.request
import urllib.parse
import uuid

BASE = os.environ.get('CIVICPAY_URL', 'http://127.0.0.1:5080').rstrip('/')
KEY = os.environ.get('CIVICPAY_API_KEY', '')
TODAY = datetime.datetime.now(datetime.timezone.utc).date().isoformat()
SUFFIX = uuid.uuid4().hex[:8].upper()
CODE = 'AUDIT-' + SUFFIX
PAYMENT_HEADER = 'municipalityCode,accountNumber,paymentType,amount,transactionDate,externalReference\n'
ACCOUNT_HEADER = 'municipalityCode,accountNumber,paymentType,balance\n'
evidence = {'baseUrl': BASE, 'municipality': CODE, 'executedAtUtc': datetime.datetime.now(datetime.timezone.utc).isoformat(), 'checks': [], 'reports': {}}


def call(path, method='GET', body=None, expected=200, content_type='application/json', error=None, json_response=True):
    if isinstance(body, dict):
        body = json.dumps(body).encode()
    headers = {'X-Api-Key': KEY}
    if body is not None:
        headers['Content-Type'] = content_type
    request = urllib.request.Request(urllib.parse.urljoin(BASE + "/", path), data=body, method=method, headers=headers)
    try:
        response = urllib.request.urlopen(request, timeout=45)
    except urllib.error.HTTPError as failure:
        response = failure
    with response:
        status, raw, response_headers = response.code, response.read(), response.headers
    assert status in (expected if isinstance(expected, tuple) else (expected,)), (path, status, raw.decode(errors='replace'))
    assert response_headers.get('X-Correlation-ID'), path
    data = json.loads(raw) if json_response else raw.decode()
    if error:
        assert data['success'] is False and data['errorCode'] == error, data
        assert data['correlationId'] == response_headers['X-Correlation-ID'], data
    return status, data, response_headers


def get(path):
    return call(path)[1]


def upload(content, kind='payments', expected=201, error=None):
    if isinstance(content, str):
        content = content.encode('utf-8')
    boundary = 'audit-' + uuid.uuid4().hex
    body = (f'--{boundary}\r\nContent-Disposition: form-data; name="kind"\r\n\r\n{kind}\r\n--{boundary}\r\nContent-Disposition: form-data; name="file"; filename="audit.csv"\r\nContent-Type: text/csv\r\n\r\n'.encode() + content + f'\r\n--{boundary}--\r\n'.encode())
    return call('/api/imports', 'POST', body, expected, 'multipart/form-data; boundary=' + boundary, error)[1]


def payment(account, amount, reference):
    return {'municipalityCode': CODE, 'accountNumber': account, 'paymentType': 'Utility', 'amount': amount, 'transactionDate': TODAY, 'externalReference': reference}


def check(name):
    evidence['checks'].append(name)


assert get('/api/health')['status'] == 'Healthy'
configuration = {'municipalityCode': CODE, 'municipalityName': 'Synthetic Audit Municipality', 'currency': 'CAD', 'allowPartialPayments': True, 'minimumPayment': 5, 'acceptedPaymentTypes': ['Utility', 'Permit']}
_, created, location = call('/api/municipalities', 'POST', configuration, 201)
assert get(location['Location']) == created
assert get('/api/municipalities/' + CODE + '/payment-types') == ['Permit', 'Utility']
configuration.update(version=created['version'], minimumPayment=10)
_, updated, _ = call('/api/municipalities/' + CODE, 'PUT', configuration)
call('/api/municipalities/' + CODE, 'PUT', configuration, 409, error='CONFIGURATION_CONFLICT')
assert get('/api/municipalities/' + CODE)['minimumPayment'] == 10
check('Health; configuration create/read/update; enabled types; stale-version 409')

accounts = ACCOUNT_HEADER + f'{CODE},A,Utility,100\n{CODE},B,Utility,60\n{CODE},C,Utility,150\n'
account_report = upload(accounts, 'accounts')
assert (account_report['importedRecordCount'], account_report['totalImportedAmount'], account_report['difference'], account_report['status']) == (3, 310, 0, 'RECONCILED')
evidence['reports']['accounts'] = account_report
repeated_accounts = upload(accounts, 'accounts')
assert repeated_accounts['skippedRecordCount'] == 3 and repeated_accounts['totalImportedAmount'] == 0
conflicting_account = upload(ACCOUNT_HEADER + f'{CODE},A,Utility,200\n', 'accounts')
assert conflicting_account['status'] == 'FAILED'
assert get(f"/api/imports/{conflicting_account['batchId']}/records")['items'][0]['errorCode'] == 'ACCOUNT_CONFLICT'
assert get('/api/municipalities/' + CODE)['version'] != updated['version']
check('Account import; exact account skip; conflicting account rejected; version advances')

payments = PAYMENT_HEADER + f'{CODE},A,Utility,25,{TODAY},VALID-1\n{CODE},B,Utility,60,{TODAY},VALID-2\n{CODE},C,Utility,50,{TODAY},VALID-3\n'
valid = upload(payments)
assert (valid['importedRecordCount'], valid['totalSourceAmount'], valid['totalImportedAmount'], valid['difference'], valid['status']) == (3, 135, 135, 0, 'RECONCILED')
assert get(f"/api/imports/{valid['batchId']}/reconciliation") == valid
repeated = upload(payments)
assert (repeated['skippedRecordCount'], repeated['totalImportedAmount'], repeated['difference'], repeated['status']) == (3, 0, 135, 'WARNING')
mixed = upload(PAYMENT_HEADER + f'{CODE},A,Utility,10,{TODAY},MIXED-GOOD\n{CODE},A,Utility,-1,{TODAY},NEGATIVE\n{CODE},UNKNOWN,Utility,30,{TODAY},MISSING\n{CODE},A,ParkingTicket,20,{TODAY},TYPE\n{CODE},A,Utility,nope,{TODAY},AMOUNT\n')
assert (mixed['importedRecordCount'], mixed['rejectedRecordCount'], mixed['totalSourceAmount'], mixed['totalImportedAmount'], mixed['difference'], mixed['sourceAmountComplete'], mixed['status']) == (1, 4, 59, 10, 49, False, 'WARNING')
failed = upload(PAYMENT_HEADER + f'UNKNOWN,A,Utility,10,{TODAY},UNKNOWN\n')
assert failed['status'] == 'FAILED' and failed['rejectedRecordCount'] == 1
for name, report in [('validPayments', valid), ('duplicatePayments', repeated), ('mixedPayments', mixed), ('failedPayments', failed)]:
    evidence['reports'][name] = report
records = get(f"/api/imports/{mixed['batchId']}/records?page=2&pageSize=2")
assert records['total'] == 5 and [x['rowNumber'] for x in records['items']] == [4, 5]
check('Successful, mixed, all-rejected and duplicate imports; exact totals and row pagination')

upload('wrong,header\na,b\n', expected=400, error='INVALID_CSV_HEADER')
upload(PAYMENT_HEADER + '"unterminated', expected=400, error='INVALID_CSV')
upload(ACCOUNT_HEADER.encode() + b'\xff', 'accounts', 400, 'INVALID_CSV_ENCODING')
upload(b'x' * 2_000_001, 'accounts', 413, 'IMPORT_TOO_LARGE')
upload(ACCOUNT_HEADER + f'{CODE},D,Utility,50\n' * 5001, 'accounts', 413, 'IMPORT_TOO_LARGE')
check('Bad header, malformed quotes, invalid UTF-8, oversize bytes and excess rows rejected')

with concurrent.futures.ThreadPoolExecutor(max_workers=8) as executor:
    retries = list(executor.map(lambda _: call('/api/payments', 'POST', payment('C', 100, 'FULL-RETRY'), (200, 201)), range(8)))
assert sum(status == 201 for status, _, _ in retries) == 1
assert len({data['transactionId'] for _, data, _ in retries}) == 1
assert sum(data['replayed'] for _, data, _ in retries) == 7
transaction_id = retries[0][1]['transactionId']
assert get('/api/payments/' + transaction_id)['amount'] == 100
call('/api/payments', 'POST', payment('C', 99, 'FULL-RETRY'), 409, error='DUPLICATE_REFERENCE')
with concurrent.futures.ThreadPoolExecutor(max_workers=2) as executor:
    competing = list(executor.map(lambda ref: call('/api/payments', 'POST', payment('A', 40, ref), (201, 422)), ['DISTINCT-1', 'DISTINCT-2']))
assert sorted(status for status, _, _ in competing) == [201, 422]
assert next(data for status, data, _ in competing if status == 422)['errorCode'] == 'OVERPAYMENT'
for reference in ['case-reference', 'CASE-REFERENCE']:
    call('/api/payments', 'POST', payment('A', 10, reference), 201)
check('Eight full-balance retries; one debit; conflicting retry 409; concurrent distinct payments prevent overdraft; case-sensitive references')

call('/api/payments', 'POST', b'{bad-json', 400, error='INVALID_REQUEST')
call('/api/payments', 'POST', b'{}', 415, 'text/plain', 'UNSUPPORTED_MEDIA_TYPE')
call('/api/payments', 'DELETE', expected=405, error='METHOD_NOT_ALLOWED')
call('/api/missing', expected=404, error='ENDPOINT_NOT_FOUND')
call('/api/monitoring/accounts?page=2147483647&pageSize=200', expected=400, error='INVALID_PAGINATION')
call('/api/monitoring?from=2026-02-01&to=2026-01-01', expected=400, error='INVALID_DATE_RANGE')
call('/api/payments', 'POST', payment('A', 1.001, 'PRECISION'), 422, error='INVALID_AMOUNT')
check('Structured 400/404/405/409/413/415/422 responses and correlation IDs')

balances = get('/api/monitoring/accounts?municipality=' + CODE)
assert {item['accountNumber']: item['balance'] for item in balances['items']} == {'A': 5, 'B': 0, 'C': 0}
snapshot = get(f'/api/monitoring?municipality={CODE}&from={TODAY}&to={TODAY}')
assert all(t['municipalityCode'] == CODE for t in snapshot['transactions'])
assert snapshot['metrics']['totalAmount'] == 305
assert snapshot['metrics']['successfulTransactions'] == 8
assert get('/api/monitoring?municipality=' + CODE + '&status=Rejected')['transactions'] == []
assert get('/api/monitoring?municipality=' + CODE + '&status=Accepted')['errors'] == []
check('Monitoring municipality, UTC dates and outcomes; CAD 305 total; final balances A=5/B=0/C=0')

swagger = get('/swagger/v1/swagger.json')
for path in ['/api/payments', '/api/municipalities', '/api/imports', '/api/monitoring']:
    assert path in swagger['paths']
assert 'PaymentRequest' in swagger['components']['schemas']
assert swagger['components']['securitySchemes']['ApiKey']['name'] == 'X-Api-Key'
for path in ['/', '/app.js', '/styles.css', '/swagger/index.html']:
    _, content, headers = call(path, json_response=False)
    assert content
    if path == '/':
        assert 'CivicPay' in content and 'Content-Security-Policy' in headers
check('Swagger paths/schema/API-key contract; dashboard HTML/JS/CSS and CSP served')

path = os.environ.get('CIVICPAY_AUDIT_DATABASE')
if path:
    with sqlite3.connect(path) as db:
        for report in evidence['reports'].values():
            batch = report['batchId'].replace('-', '').upper()
            expected, status = db.execute("SELECT ExpectedRecordCount, Status FROM ImportBatches WHERE upper(replace(Id,'-',''))=?", (batch,)).fetchone()
            rows = db.execute("SELECT SourceAmount, ImportedAmount, Status FROM ImportRecords WHERE upper(replace(ImportBatchId,'-',''))=?", (batch,)).fetchall()
            source = sum((Decimal(str(r[0])) for r in rows if r[0] is not None), Decimal(0))
            imported = sum((Decimal(str(r[1])) for r in rows), Decimal(0))
            assert expected == report['sourceRecordCount']
            assert source == Decimal(str(report['totalSourceAmount']))
            assert imported == Decimal(str(report['totalImportedAmount']))
            assert source - imported == Decimal(str(report['difference']))
            assert (len(rows) == expected and all(r[0] is not None for r in rows)) == report['sourceAmountComplete']
            assert status == 'Completed'
        rows = db.execute('SELECT a.AccountNumber,a.Balance FROM Accounts a JOIN Municipalities m ON a.MunicipalityId=m.Id WHERE m.Code=?', (CODE,)).fetchall()
        assert {number: Decimal(str(balance)) for number, balance in rows} == {'A': Decimal(5), 'B': Decimal(0), 'C': Decimal(0)}
        assert db.execute('SELECT COUNT(*) FROM Transactions t JOIN Municipalities m ON t.MunicipalityId=m.Id WHERE m.Code=? AND ExternalReference=?', (CODE, 'FULL-RETRY')).fetchone()[0] == 1
    check('Independent SQLite queries confirm report amounts/completeness, balances and exactly one full-retry transaction')
else:
    evidence['notVerified'] = ['Independent database queries: CIVICPAY_AUDIT_DATABASE not set']
print(json.dumps(evidence, indent=2))
