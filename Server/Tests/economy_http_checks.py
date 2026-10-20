#!/usr/bin/env python3
"""HTTP/admin/migration checks in an isolated database. Never logs credentials or alters the source DB."""
import argparse, base64, copy, hashlib, http.cookiejar, json, os, secrets, shutil, socket, sqlite3, subprocess, tempfile, time, urllib.error, urllib.request, uuid
from pathlib import Path

parser=argparse.ArgumentParser()
parser.add_argument('--dotnet')
parser.add_argument('--server')
parser.add_argument('--dll')
parser.add_argument('--legacy-db')
parser.add_argument('--collision')
parser.add_argument('--skip-combat',action='store_true',help='Compatibilidade: combate é validado pelos self-tests e PlayMode.')
parser.add_argument('--port',type=int,default=18093)
args=parser.parse_args()
root=Path(__file__).resolve().parents[1]
if not args.server and not args.dll:
    executable=root/'Build'/'Server'
    if not executable.is_file():parser.error('Compile o servidor ou informe --server ou --dotnet e --dll.')
    args.server=str(executable)
if not args.server:
    args.dotnet=args.dotnet or shutil.which('dotnet')
    if not args.dotnet and (root/'.dotnet'/'dotnet').is_file():args.dotnet=str(root/'.dotnet'/'dotnet')
    if not args.dotnet:parser.error('Informe --dotnet para executar a DLL ou use --server com o executável compilado.')
base='http://127.0.0.1:'+str(args.port)
assertions=0

def check(ok,why):
    global assertions
    assertions+=1
    if not ok: raise AssertionError(why)

def states(db):
    with sqlite3.connect(db) as connection:
        connection.execute('PRAGMA query_only=ON')
        return {pid:json.loads(raw) for pid,raw in connection.execute('SELECT player_id,state_json FROM players')}

def req(path,body=None,method=None,expected=200,admin=False,headers=None):
    extra={'Content-Type':'application/json'}
    if admin:extra['X-Admin-Request']='1'
    if headers:extra.update(headers)
    request=urllib.request.Request(base+path,data=None if body is None else json.dumps(body).encode(),headers=extra,method=method or ('GET' if body is None else 'POST'))
    try:
        with opener.open(request,timeout=15) as response:status,data=response.status,response.read()
    except urllib.error.HTTPError as error:status,data=error.code,error.read()
    check(status==expected,'HTTP inesperado em '+path+': '+str(status))
    if not data:return {}
    if data.startswith(b'<!doctype'):return data.decode()
    return json.loads(data)

def connect(key):return req('/v1/connect',{'protocolVersion':2,'deviceKey':key,'displayName':'Validação '+hashlib.sha256(key.encode()).hexdigest()[:10]})

def send(token,commands):return req('/v1/commands',{'protocolVersion':2,'commands':commands,'afterEventId':0},headers={'Authorization':'Bearer '+token})

def cmd(kind,**fields):return {'commandId':str(uuid.uuid4()),'type':kind,**fields}

def start(directory,db):
    with socket.socket() as guard:
        guard.setsockopt(socket.SOL_SOCKET,socket.SO_REUSEADDR,1)
        try:guard.bind(('127.0.0.1',args.port))
        except OSError:raise RuntimeError('Porta de teste ocupada; escolha outra com --port. Nenhum servidor existente foi acessado.') from None
    env=dict(os.environ,HOST='127.0.0.1',PORT=str(args.port),DB_PATH=str(db),COLLISION_PATH=str(Path(args.collision).resolve() if args.collision else root/'world-collision.json'),DOTNET_CLI_TELEMETRY_OPTOUT='1',DOTNET_CLI_HOME=str(directory/'dotnet-home'))
    log=open(directory/'server.log','ab')
    process=subprocess.Popen([args.server] if args.server else [args.dotnet,args.dll],cwd=root,env=env,stdout=log,stderr=subprocess.STDOUT)
    for _ in range(150):
        if process.poll() is not None:raise RuntimeError('Servidor isolado não iniciou; consulte server.log sem publicar credenciais.')
        try:
            with urllib.request.urlopen(base+'/health',timeout=1) as response:
                if response.status==200:return process,log
        except (OSError,urllib.error.URLError):pass
        time.sleep(.1)
    process.terminate();raise RuntimeError('Servidor não ficou pronto')

def stop(process,log):
    process.terminate()
    try:process.wait(timeout=10)
    except subprocess.TimeoutExpired:process.kill();process.wait()
    log.close()

with tempfile.TemporaryDirectory(prefix='gdn-economy-http-') as temporary:
    directory=Path(temporary);db=directory/'characters.db';before={}
    if args.legacy_db:
        with sqlite3.connect(args.legacy_db) as source,sqlite3.connect(db) as target:
            source.execute('PRAGMA query_only=ON');source.backup(target)
        before=states(db)
    opener=urllib.request.build_opener(urllib.request.HTTPCookieProcessor(http.cookiejar.CookieJar()))
    process,log=start(directory,db)
    try:
        health=req('/health');check(health['protocolVersion']==2,'protocolo2 ativo')
        after=states(db)
        for pid,old in before.items():
            a,b=old['character'],after[pid]['character']
            for key in ['level','experience','attackPoints','defensePoints','intelligencePoints','unspentPoints','health','swordProficiencyLevel','inventoryItemIds']:
                check(a[key]==b[key],'migração alterou '+key)
            check([(i['slot'],i['itemId']) for i in a['equipped']]==[(i['slot'],i['itemId']) for i in b['equipped']],'migração alterou slots')
            check(b['schemaVersion']==2 and len(b['itemInstances'])==len(a['inventoryItemIds'])+len(a['equipped']),'migração completa de instâncias')
            check(b['coins']==0,'migração inventou moedas')
        if before:check(bool(list(directory.glob('*.pre-v2-*.bak'))),'backup automático pré-migração ausente')
        req('/admin/api/state',expected=401)
        req('/admin/api/login',{'secret':'invalid'},expected=403)
        req('/admin/api/login',{'secret':'invalid'},expected=401,admin=True)
        secret=(directory/'admin-secret.txt').read_text().strip()
        req('/admin/api/login',{'secret':secret},admin=True)
        panel=req('/admin');check('Economia e mundo' in panel and 'Chefões e horários' in panel,'painel HTML e agenda de chefões servidos')
        state=req('/admin/api/state',admin=True)
        check('bossSchedules' in state['active'] and isinstance(state.get('bossWindows'),list),'contrato administrativo de agenda ausente')
        req('/admin/api/draft',{'baseRevision':state['draftRevision'],'config':state['draft']},'PUT',403,True,{'Origin':'https://untrusted.invalid'})
        key=base64.b64encode(secrets.token_bytes(32)).decode();player=connect(key)
        check(player['character']['coins']==0 and len(player['character']['itemInstances'])==12,'starter inventa riqueza')
        check(all('visualKind' in m and 'level' in m and 'damage' in m and 'targetId' in m for m in player['monsters']),'snapshot sem perfil e alvo autoritativo')
        invalid_target=send(player['sessionToken'],[cmd('ability',ability=0,targetId='forged-monster')]);check(not invalid_target['results'][0]['accepted'],'alvo inventado aceito por HTTP')
        req('/v1/connect',{'protocolVersion':1,'deviceKey':key},expected=400)
        try_buy=cmd('buy',npcId='npc-ferreiro',offerId='offer-steel-sword',quoteVersion=player['economyVersion'])
        rejected=send(player['sessionToken'],[try_buy]);check(not rejected['results'][0]['accepted'] and rejected['character']['coins']==0,'compra sem saldo/proximidade aceita')
        config=copy.deepcopy(state['draft']);sword=next(i for i in config['items'] if i['definitionId']=='steel-sword');sword['damage']+=1
        saved=req('/admin/api/draft',{'baseRevision':state['draftRevision'],'config':config,'reason':'Validação de revisão automática','requestId':str(uuid.uuid4())},'PUT',admin=True)
        check(next(i for i in saved['draft']['items'] if i['definitionId']=='steel-sword')['definitionRevision']>sword['definitionRevision'],'atributos não receberam revisão')
        req('/admin/api/draft',{'baseRevision':state['draftRevision'],'config':config},'PUT',409,True)
        valid=req('/admin/api/validate',{},admin=True);check(valid['valid'],'rascunho normalizado inválido')
        sim=req('/admin/api/simulate',{'kills':1000,'killRate':45},admin=True);check(len(sim['monsters'])>=4,'simulação incompleta')
        publish={'baseRevision':saved['draftRevision'],'reason':'Publicação isolada de integração','requestId':str(uuid.uuid4()),'activateAtUnixMs':0}
        published=req('/admin/api/publish',publish,admin=True);check(published['active']['revision']>state['active']['revision'],'publicação não ativou')
        repeated=req('/admin/api/publish',publish,admin=True);check(len(repeated['history'])==len(published['history']),'retry duplicou versão')
        check(send(player['sessionToken'],[])['economyVersion']==published['active']['revision'],'snapshot não refletiu publicação')
        rolled=req('/admin/api/rollback',{'version':state['active']['revision'],'reason':'Reativação isolada de integração','requestId':str(uuid.uuid4())},admin=True)
        check(rolled['active']['revision']==state['active']['revision'],'rollback não ativou versão anterior')
        req('/admin/api/logout',{},admin=True);req('/admin/api/state',expected=401)
        first=send(player['sessionToken'],[])['character']
    finally:stop(process,log)
    opener=urllib.request.build_opener(urllib.request.HTTPCookieProcessor(http.cookiejar.CookieJar()))
    process,log=start(directory,db)
    try:
        player=connect(key);check(player['character']==first,'reinício mudou patrimônio ou progresso')
        final=states(db)
        for pid,old in before.items():
            b=final[pid]['character'];check(b['inventoryItemIds']==old['character']['inventoryItemIds'],'publicações alteraram mochila legada')
        with sqlite3.connect(db) as c:check(c.execute('PRAGMA integrity_check').fetchone()[0]=='ok','banco final inconsistente')
    finally:stop(process,log)
    print('PASS:',assertions,'verificações HTTP, autenticação, CSRF, migração real, versões e reinício; banco original preservado.')
