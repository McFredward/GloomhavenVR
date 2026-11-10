"""Build declared GL fragment ports with the COMPLETE original shader property ABI.
Native Windows programs are not executed: property order/types, every saved material
value/texture, original authored graph and production import/validation ARE executed.
"""
import json

def write_ports(inputs,destination):
 bank=json.loads((inputs/'native-materials.json').read_text())
 view={k:[dict(v,key=n) for n,v in bank[k].items()] for k in ('materials','textures')}
 view['shaders']=[{'key':key,'name':v['name'],'properties':[{'name':p['m_Name'],'type':p['m_Type']} for p in v['properties']]} for key,v in bank['shaders'].items()]
 (inputs/'native-material-list.json').write_text(json.dumps(view,indent=2))
 for key,shader in bank['shaders'].items():
  props=[]
  for p in shader['properties']:
   n=p['m_Name'];t=p['m_Type'];d=[p['m_DefValue['+str(i)+']'] for i in range(4)]
   if t in (0,1):kind='Color' if t==0 else 'Vector';value='('+','.join(map(str,d))+')'
   elif t in (2,3):kind='Float' if t==2 else 'Range('+str(d[1])+','+str(d[2])+')';value=str(d[0])
   elif t==4:kind='2D';value=json.dumps(p['m_DefTexture']['m_DefaultName'] or 'white')+' {}'
   else:raise AssertionError(p)
   props.append(n+' ('+json.dumps(p['m_Description'])+', '+kind+') = '+value)
  state=shader['native_pass_state'];blend=state['rtBlend0'];blendnames={0:'Zero',1:'One',2:'DstColor',3:'SrcColor',4:'OneMinusDstColor',5:'SrcAlpha',6:'OneMinusSrcColor',7:'DstAlpha',8:'OneMinusDstAlpha',9:'SrcAlphaSaturate',10:'OneMinusSrcAlpha'}
  src=blendnames[int(blend['srcBlend']['val'])];dst=blendnames[int(blend['destBlend']['val'])]
  # Native render-state metadata is retained above; these scalar states follow it.
  ztest=state['zTest'];z='['+ztest['name']+']' if ztest['name']!='<noninit>' else {0:'Off',1:'Never',2:'Less',3:'Equal',4:'LEqual',5:'Greater',6:'NotEqual',7:'GEqual',8:'Always'}[int(ztest['val'])]
  names={p['m_Name'] for p in shader['properties']};tint='_Color' if '_Color' in names else '_TintColor' if '_TintColor' in names else None
  noise='_AngularNoiseMask' in names
  cg='sampler2D _MainTex;'+('float4 '+tint+';' if tint else '')+('sampler2D _AngularNoiseMask;' if noise else '')
  fragment='float4 c=tex2D(_MainTex,i.uv)*i.color;'+('c*='+tint+';' if tint else '')+('c.rgb*=.75+.25*tex2D(_AngularNoiseMask,i.uv).r;' if noise else '')+'return c;'
  text='Shader '+json.dumps(shader['name'])+' { Properties {\n'+'\n'.join(props)+'\n} SubShader { Tags { "Queue"="Transparent" "RenderType"="Transparent" } Cull Off ZWrite '+('On' if state['zWrite']['val'] else 'Off')+' ZTest '+z+' Blend '+src+' '+dst+' Pass { CGPROGRAM\n#pragma vertex vert\n#pragma fragment frag\n#include "UnityCG.cginc"\n'+cg+'\nstruct A {float4 vertex:POSITION;float2 uv:TEXCOORD0;float4 color:COLOR;}; struct V {float4 vertex:SV_POSITION;float2 uv:TEXCOORD0;float4 color:COLOR;}; V vert(A v){V o;o.vertex=UnityObjectToClipPos(v.vertex);o.uv=v.uv;o.color=v.color;return o;} float4 frag(V i):SV_Target {'+fragment+'}\nENDCG\n} } }\n'
  (destination/(key+'.shader')).write_text(text)
