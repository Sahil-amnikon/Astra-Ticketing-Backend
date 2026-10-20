const fs = require('node:fs'), path = require('node:path'), ts = require('../../../releases/astra-admin-service-config-20261013/node_modules/typescript'), {spawnSync} = require('node:child_process');
const root=path.resolve(__dirname,'..'),source=path.resolve(root,'../../releases/astra-admin-service-config-20261013');
const routes=[];
const previousFile=path.join(root,'inventory/routes.json');
const previous=new Map((fs.existsSync(previousFile)?JSON.parse(fs.readFileSync(previousFile,'utf8')):[]).map(r=>[r.method+' '+r.path,r]));
for(const name of fs.readdirSync(path.join(source,'apps/api/src')).filter(n=>n.endsWith('.ts'))){
  const file=path.join(source,'apps/api/src',name),text=fs.readFileSync(file,'utf8'),ast=ts.createSourceFile(file,text,ts.ScriptTarget.Latest,true);
  for(const cls of ast.statements.filter(ts.isClassDeclaration)){
    const decorators=ts.getDecorators(cls)??[];const controller=decorators.map(d=>d.expression).find(e=>ts.isCallExpression(e)&&e.expression.getText(ast)==='Controller');if(!controller)continue;
    const prefix=controller.arguments[0]?.text??'';
    for(const member of cls.members){
      for(const d of ts.getDecorators(member)??[]){const e=d.expression;if(!ts.isCallExpression(e))continue;const verb=e.expression.getText(ast);if(!['Get','Post','Patch','Put','Delete'].includes(verb))continue;
        routes.push({method:verb.toUpperCase(),path:'/'+[prefix,e.arguments[0]?.text].filter(Boolean).join('/'),source:'apps/api/src/'+name,handler:member.name?.getText(ast),status:'pending'});
      }
    }
  }
}
fs.mkdirSync(path.join(root,'inventory'),{recursive:true});fs.writeFileSync(path.join(root,'inventory/routes.json'),JSON.stringify(routes.map(r=>({...r,...(previous.get(r.method+' '+r.path)??{})})),null,2));
const snapshot=spawnSync('ssh',['-o','BatchMode=yes','-o','ConnectTimeout=10','tktweb','cat /home/itadmin/astra-dotnet-schema.json'],{encoding:'utf8'});
if(snapshot.status!==0){console.error('Schema snapshot read failed');process.exit(1);}
const ownerInventory=path.join(root,'inventory/schema.owner.json');
const schema=JSON.parse(fs.existsSync(ownerInventory)?fs.readFileSync(ownerInventory,'utf8'):snapshot.stdout);
fs.writeFileSync(path.join(root,'inventory/schema.json'),JSON.stringify(schema,null,2));
console.log(JSON.stringify({routes:routes.length,tables:Object.keys(schema.counts).length,columns:schema.columns.length,schemaVersion:schema.schemaVersion,readOnly:true}));
