#!/usr/bin/env python3
"""Standard-library HTTP smoke checks against a running synthetic environment."""
import os, json, urllib.request, urllib.error, uuid
from pathlib import Path
base=os.environ.get('CIVICPAY_URL','http://127.0.0.1:5080')
key=os.environ.get('CIVICPAY_API_KEY','')
def request(path,body=None,content_type='application/json',expected=200):
 headers={'X-Api-Key':key}
 if body is not None: headers['Content-Type']=content_type
 req=urllib.request.Request(base+path,data=body,headers=headers)
 try:
  with urllib.request.urlopen(req) as res: code,data=res.status,res.read()
 except urllib.error.HTTPError as e: code,data=e.code,e.read()
 assert code==expected,(path,code,data.decode())
 return json.loads(data)
def upload(path,kind,transform=False):
 boundary='civicpay-'+uuid.uuid4().hex
 csv=Path(path).read_bytes()
 if transform:
  for original,replacement in mappings.items(): csv=csv.replace(original.encode(),replacement.encode())
 body=(f'--{boundary}\r\nContent-Disposition: form-data; name="kind"\r\n\r\n{kind}\r\n--{boundary}\r\nContent-Disposition: form-data; name="file"; filename="synthetic.csv"\r\nContent-Type: text/csv\r\n\r\n'.encode()+csv+f'\r\n--{boundary}--\r\n'.encode())
 return request('/api/imports',body,'multipart/form-data; boundary='+boundary,201)
assert request('/api/health')['status']=='Healthy'
assert len(request('/api/municipalities'))>=3
root=Path(__file__).resolve().parents[1]
suffix=uuid.uuid4().hex[:8]
mappings={name:name+'-'+suffix for name in ['LEGACY-PT-1001','LEGACY-PK-1001','LEGACY-UT-1001','LEGACY-PAY-1001','LEGACY-PAY-1002','LEGACY-PAY-1003']}
accounts=upload(root/'data/valid/accounts.csv','accounts',True)
assert accounts['rejectedRecordCount']==0
valid=upload(root/'data/valid/payments.csv','payments',True)
assert valid['rejectedRecordCount']==0
repeat=upload(root/'data/valid/payments.csv','payments',True)
assert repeat['importedRecordCount']==0 and repeat['skippedRecordCount']==3
bad=upload(root/'data/invalid/payments.csv','payments',True)
assert bad['rejectedRecordCount']>=6
assert request('/api/imports/'+valid['batchId']+'/reconciliation')['status']==valid['status']
account='SMOKE-'+uuid.uuid4().hex[:8]
# Create an isolated account so the smoke test can run repeatedly.
header='municipalityCode,accountNumber,paymentType,balance\n'
path=Path('/tmp')/('civicpay-'+uuid.uuid4().hex+'.csv')
try:
 path.write_text(header+f'DEMO-COUNTY,{account},Utility,100\n')
 assert upload(path,'accounts')['status']=='RECONCILED'
finally: path.unlink(missing_ok=True)
payload=json.dumps({'municipalityCode':'DEMO-COUNTY','accountNumber':account,'paymentType':'Utility','amount':25,'transactionDate':'2026-01-01','externalReference':'SMOKE-'+uuid.uuid4().hex}).encode()
first=request('/api/payments',payload,expected=201)
retry=request('/api/payments',payload)
assert retry['replayed'] and retry['transactionId']==first['transactionId']
changed=json.loads(payload);changed['amount']=30
assert request('/api/payments',json.dumps(changed).encode(),expected=409)['errorCode']=='DUPLICATE_REFERENCE'
assert request('/api/payments',b'{invalid',expected=400)['errorCode']=='INVALID_REQUEST'
assert 'paths' in request('/swagger/v1/swagger.json')
assert request('/api/monitoring')['metrics']['successfulTransactions']>0
print(json.dumps({'health':'passed','accounts':accounts,'validImport':valid,'repeatedImport':repeat,'invalidImport':bad,'idempotency':'passed','malformedJson':'passed','swagger':'passed'},indent=2))
