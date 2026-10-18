#define _GNU_SOURCE
#define GHVR_QUALITY_TEST
#include "../../tools/steamlink-stream-quality.c"
#include <stdlib.h>
#include <unistd.h>
#include <sys/wait.h>
static bool transcript;
static int32_t expected_request=3072,seen_width;
static bool absent[KEY_COUNT],baseline_absent[KEY_COUNT];
static int setting_error,setting_error_key,manual_absence,remove_fail,remove_ignored;
static unsigned removals;
static unsigned checks,reads,writes,identities,ready_calls,shutdowns,inits,gets;
static int32_t values[KEY_COUNT],baseline[KEY_COUNT];static int write_keys[64];
static int read_fail,write_fail,write_fail_applies,identity_change,clamp_width,manual_key,manual_value,close_error;
static int stage,init_error,get_error,readback_fail,fail_after_ready,restore_fail,change_at_read;
static bool is_connected;static int32_t class_value;
static const char *tracking,*model,*registered;
static struct System022 sysapi;static struct Settings003 settings;
static void check(bool b){checks++;if(!b){fprintf(stderr,"quality check%u failed (reads%u,writes%u,stage%d)\n",checks,reads,writes,stage);exit(1);}}
static int key_id(const char *s){for(int i=0;i<KEY_COUNT;i++)if(!strcmp(s,keys[i]))return i;check(false);return -1;}
static int32_t device_class(uint32_t i){check(i==0);return class_value;}
static bool connected(uint32_t i){check(i==0);identities++;return is_connected;}
static uint32_t property_read(uint32_t i,int32_t key,char *out,uint32_t cap,int32_t *error){
    check(i==0&&cap==STRING_CAP&&(key==1000||key==1001||key==1036));*error=0;
    const char *s=key==1000?tracking:key==1001?model:registered;
    if(identity_change&&identities>=(unsigned)identity_change&&key==1001)s="Quest 3";
    size_t len=strlen(s);memcpy(out,s,len+1);return (uint32_t)len+1;
}
static int32_t read_setting(const char *s,const char *k,int32_t *e,bool boolean){
    check(!strcmp(s,section));int key=key_id(k);check(boolean==(key==AUTOMATIC||key==SHARPENING));reads++;*e=0;
    if(change_at_read&&reads==(unsigned)change_at_read){values[key]=key==WIDTH?2816:key==FOVEATION?2:!values[key];absent[key]=false;}
    if((read_fail&&reads==(unsigned)read_fail)||(readback_fail&&writes==1&&!ready_calls)){
        readback_fail=0;*e=3;return 0;
    }
    if(fail_after_ready&&ready_calls){fail_after_ready=0;*e=3;return 0;}
    if(key==setting_error_key){*e=setting_error;return 0;}
    if(absent[key]){*e=5;return 0;}
    return values[key];
}
static int32_t get_int(const char *s,const char *k,int32_t *e){return read_setting(s,k,e,false);}
static bool get_bool(const char *s,const char *k,int32_t *e){return read_setting(s,k,e,true)!=0;}
static void write_setting(const char *s,const char *k,int32_t v,int32_t *e,bool boolean){
    check(!strcmp(s,section));int key=key_id(k);check(boolean==(key==AUTOMATIC||key==SHARPENING));
    check(reads>=4);check(writes<64);write_keys[writes]=key;writes++;*e=0;
    if((write_fail&&writes==(unsigned)write_fail)||(restore_fail&&ready_calls)){
        restore_fail=0;*e=3;if(!write_fail_applies)return;
    }
    absent[key]=false;values[key]=(key==WIDTH&&clamp_width&&v==3072)?clamp_width:v;
}
static void set_int(const char *s,const char *k,int32_t v,int32_t *e){write_setting(s,k,v,e,false);}
static void set_bool(const char *s,const char *k,bool v,int32_t *e){write_setting(s,k,(int32_t)v,e,true);}
static void remove_key(const char *s,const char *k,int32_t *error){
    check(!strcmp(s,section));int key=key_id(k);removals++;*error=0;
    if(remove_fail){remove_fail=0;*error=6;return;}
    if(remove_ignored){remove_ignored=0;return;}
    absent[key]=true;values[key]=0;
}
static uint32_t fake_init(int32_t *e,int32_t app,const char *startup){check(stage==0&&app==3&&startup==NULL);stage=1;inits++;*e=init_error;return init_error?0:41;}
static void *fake_get(const char *name,int32_t *e){
    check(stage==1&&init_error==0);gets++;*e=get_error;
    if(!strcmp(name,"FnTable:IVRSystem_022"))return &sysapi;
    check(!strcmp(name,"FnTable:IVRSettings_003"));return &settings;
}
static void fake_shutdown(void){check(stage==1&&init_error==0);stage=2;shutdowns++;}
static const char *await_close(const struct Lease *lease,void *context){
    (void)context;ready_calls++;check(lease->requested==expected_request);seen_width=lease->applied[WIDTH];
    if(transcript){const char *error=print_acquired(lease);if(error)return error;}check(values[FOVEATION]==1&&values[SHARPENING]==0&&values[AUTOMATIC]==0);
    check(lease->applied[WIDTH]==values[WIDTH]);
    if(manual_key>=0)values[manual_key]=manual_value;
    if(manual_absence>=0)absent[manual_absence]=true;
    return close_error?"stdin_error":NULL;
}
static void reset(void){
    expected_request=3072;seen_width=0;
    reads=writes=identities=ready_calls=shutdowns=inits=gets=removals=0;
    memset(absent,0,sizeof(absent));memset(baseline_absent,0,sizeof(baseline_absent));
    setting_error=remove_fail=remove_ignored=0;setting_error_key=manual_absence=-1;
    read_fail=write_fail=write_fail_applies=identity_change=clamp_width=manual_value=close_error=0;
    stage=init_error=get_error=readback_fail=fail_after_ready=restore_fail=change_at_read=0;manual_key=-1;
    values[WIDTH]=1920;values[AUTOMATIC]=1;values[SHARPENING]=1;values[FOVEATION]=0;
    memcpy(baseline,values,sizeof(values));is_connected=true;class_value=1;tracking="vrlink";model="Steam Frame";registered="vrlink/frame";
    memset(&sysapi,0,sizeof(sysapi));sysapi.deviceClass=device_class;sysapi.connected=connected;sysapi.stringProperty=property_read;
    memset(&settings,0,sizeof(settings));settings.getInt=get_int;settings.getBool=get_bool;settings.setInt=set_int;settings.setBool=set_bool;settings.removeKey=remove_key;
}
static struct Outcome run(void){return quality_session(fake_init,fake_get,fake_shutdown,3072,await_close,NULL);}
static void restored_state(struct Outcome out){check(out.restored&&out.errors==0&&out.conflicts==0);check(!memcmp(absent,baseline_absent,sizeof(absent)));for(int key=0;key<KEY_COUNT;key++)if(!absent[key])check(values[key]==baseline[key]);check(shutdowns==1&&stage==2);}
static void validation(void){
    reset();struct Outcome out=run();check(!out.error&&out.acquired&&ready_calls==1&&writes==8);restored_state(out);
    const int expected[]={AUTOMATIC,WIDTH,SHARPENING,FOVEATION,FOVEATION,SHARPENING,WIDTH,AUTOMATIC};
    for(int n=0;n<8;n++)check(write_keys[n]==expected[n]);
    const int invalid[]={-1,0,1023,3073,4097,16385,INT32_MIN,INT32_MAX};
    const int valid[]={64,128,960,1024,1088,3072,4096,4160,8192,16384};
    for(unsigned i=0;i<sizeof(invalid)/sizeof(invalid[0]);i++)check(!valid_width(invalid[i]));
    for(unsigned i=0;i<sizeof(valid)/sizeof(valid[0]);i++)check(valid_width(valid[i]));
    reset();out=quality_session(fake_init,fake_get,fake_shutdown,3073,await_close,NULL);check(out.error&&!inits&&!writes);
    reset();out=quality_session(NULL,fake_get,fake_shutdown,3072,await_close,NULL);check(!strcmp(out.error,"exports")&&!inits);
    reset();init_error=121;out=run();check(!strcmp(out.error,"not_running")&&inits==1&&!gets&&!shutdowns&&!writes);
    reset();init_error=1;out=run();check(!strcmp(out.error,"initialization")&&!shutdowns&&!writes);
    reset();get_error=1;out=run();check(!strcmp(out.error,"system_interface")&&shutdowns==1&&!writes);
    reset();settings.getInt=NULL;out=run();check(!strcmp(out.error,"settings_interface")&&!writes&&shutdowns==1);
    for(int f=1;f<=4;f++){reset();read_fail=f;out=run();check(!strcmp(out.error,"snapshot")&&!writes&&reads==(unsigned)f&&shutdowns==1);}
    reset();values[FOVEATION]=4;out=run();check(!strcmp(out.error,"mode")&&!writes);
    reset();values[WIDTH]=16385;out=run();check(!strcmp(out.error,"snapshot_width")&&!writes);
}
static void apply_errors(void){
    struct Outcome out;
    for(int apply=0;apply<=1;apply++)for(int f=1;f<=4;f++){
        reset();write_fail=f;write_fail_applies=apply;out=run();check(!strcmp(out.error,"write")&&!out.acquired&&!ready_calls);restored_state(out);
    }
    reset();change_at_read=5;out=run();check(!strcmp(out.error,"changed_during_apply")&&!writes&&values[AUTOMATIC]==0);
    reset();change_at_read=11;out=run();check(!strcmp(out.error,"changed_during_apply")&&values[FOVEATION]==2);
    for(int key=0;key<FOVEATION;key++)check(values[key]==baseline[key]);
    reset();readback_fail=1;out=run();check(!strcmp(out.error,"readback")&&!out.acquired);restored_state(out);
    for(int f=13;f<=16;f++){reset();read_fail=f;out=run();check(!strcmp(out.error,"changed_during_apply")&&!out.acquired);restored_state(out);}
    for(int f=1;f<=8;f++){
        reset();identity_change=f;out=run();check(out.error&&!out.acquired&&!ready_calls);check(!memcmp(values,baseline,sizeof(values)));check(shutdowns==1);
    }
    reset();clamp_width=2688;out=run();check(!strcmp(out.error,"width_limited")&&!out.acquired&&!ready_calls);restored_state(out);
    reset();clamp_width=1000;out=run();check(!strcmp(out.error,"readback")&&out.conflicts==1&&values[WIDTH]==1000&&out.restored); /* Unknown change preserved. */
}
static void identity_controls(void){
    struct Outcome out;
    reset();is_connected=false;out=run();check(!strcmp(out.error,"identity")&&!writes);
    reset();class_value=2;out=run();check(!strcmp(out.error,"identity")&&!writes);
    reset();model="Quest 3";out=run();check(!strcmp(out.error,"identity")&&!writes);
    reset();tracking="VirtualDesktop";registered="";out=run();check(!strcmp(out.error,"identity")&&!writes);
    reset();tracking="cv";model="Deckard MP";registered="";out=run();check(!out.error);restored_state(out);
    reset();tracking="cv";registered="";out=run();check(!strcmp(out.error,"identity")&&!writes);
    reset();tracking="vrlink";model="Steam Frame";registered="vrlinkevil/frame";out=run();check(!out.error);restored_state(out);
    reset();tracking="other";registered="vrlinkevil/frame";out=run();check(!strcmp(out.error,"identity")&&!writes);
}
static void release_controls(void){
    struct Outcome out;
    for(int key=0;key<KEY_COUNT;key++){
        reset();manual_key=key;manual_value=key==WIDTH?2816:key==FOVEATION?2:1;out=run();
        check(!out.error&&out.restored&&values[key]==manual_value);
        check(out.conflicts==(unsigned)(key==WIDTH||key==FOVEATION));
        for(int other=0;other<KEY_COUNT;other++)if(other!=key)check(values[other]==baseline[other]);
    }
    reset();fail_after_ready=1;out=run();check(!out.error&&!out.restored&&out.errors==1); /* API outage reported; never fabricated success. */
    reset();restore_fail=1;out=run();check(!out.error&&!out.restored&&out.errors==1);
    reset();close_error=1;out=run();check(!strcmp(out.error,"stdin_error"));restored_state(out);
    for(int width=3408;width<=8192;width+=4784){
        reset();values[WIDTH]=width;values[AUTOMATIC]=0;values[FOVEATION]=1;memcpy(baseline,values,sizeof(values));
        out=run();check(!out.error&&out.acquired&&writes==2);restored_state(out);
    }
    reset();values[WIDTH]=4096;values[AUTOMATIC]=0;values[FOVEATION]=0;memcpy(baseline,values,sizeof(values));
    out=run();check(!out.error&&writes==6);restored_state(out); /* Different foveated width is not actual uniform quality. */
}
static void absence_controls(void){
    struct Outcome out;
    /* Every presence combination, including explicit false/zero and absent
     * false/zero destinations, executes the actual public API lifecycle. */
    for(unsigned mask=0;mask<16;mask++)for(int false_values=0;false_values<2;false_values++){
        reset();if(false_values){values[AUTOMATIC]=0;values[SHARPENING]=0;values[FOVEATION]=1;}
        unsigned expected=0;
        for(int key=0;key<KEY_COUNT;key++)if(mask&(1u<<key)){absent[key]=true;expected++;}
        memcpy(baseline,values,sizeof(values));memcpy(baseline_absent,absent,sizeof(absent));
        out=run();check(!out.error&&out.acquired&&ready_calls==1&&removals==expected);restored_state(out);
    }
    for(unsigned mask=1;mask<16;mask++)for(int fail=1;fail<=4;fail++)for(int applied=0;applied<2;applied++){
        reset();for(int key=0;key<KEY_COUNT;key++)if(mask&(1u<<key))absent[key]=true;
        memcpy(baseline_absent,absent,sizeof(absent));write_fail=fail;write_fail_applies=applied;
        out=run();check(!strcmp(out.error,"write")&&!out.acquired&&!ready_calls);restored_state(out);
    }
    const int actual_carriers[]={64,960,1024,3456,4096,4864,8192,16384};
    for(unsigned n=0;n<sizeof(actual_carriers)/sizeof(actual_carriers[0]);n++){
        reset();expected_request=actual_carriers[n];
        out=quality_session(fake_init,fake_get,fake_shutdown,expected_request,await_close,NULL);
        check(!out.error&&out.acquired&&seen_width==expected_request);restored_state(out);
    }
    reset();settings.removeKey=NULL;out=run();check(!strcmp(out.error,"settings_interface")&&!writes);
    for(int key=0;key<KEY_COUNT;key++)for(int error=1;error<=6;error++)if(error!=5){
        reset();setting_error_key=key;setting_error=error;out=run();
        check(!strcmp(out.error,"snapshot")&&!writes&&out.key==key&&out.api_error==error);
    }
    reset();absent[WIDTH]=baseline_absent[WIDTH]=true;remove_fail=1;out=run();
    check(out.acquired&&!out.restored&&out.errors==1&&!absent[WIDTH]);
    reset();absent[WIDTH]=baseline_absent[WIDTH]=true;remove_ignored=1;out=run();
    check(out.acquired&&!out.restored&&out.errors==1&&!absent[WIDTH]);
    for(int key=0;key<KEY_COUNT;key++){
        reset();manual_absence=key;out=run();
        check(out.acquired&&out.restored&&out.conflicts==1&&absent[key]);
        for(int other=0;other<KEY_COUNT;other++)if(other!=key)check(!absent[other]&&values[other]==baseline[other]);
    }
    reset();absent[AUTOMATIC]=true;change_at_read=5;out=run();
    check(!strcmp(out.error,"changed_during_apply")&&!writes&&!absent[AUTOMATIC]);
}
static int pipe_read_end;
static const char *await_real_parent(const struct Lease *lease,void *unused){
    (void)unused;ready_calls++;check(lease->applied[WIDTH]==3072);FILE *input=fdopen(pipe_read_end,"r");check(input!=NULL);
    const char *result=await_stdin(lease,input);check(result==NULL);check(fclose(input)==0);return result;
}
static void parent_eof_control(void){
    int pipes[2];check(pipe(pipes)==0);pid_t pid=fork();check(pid>=0);
    if(pid==0){check(close(pipes[1])==0);pipe_read_end=pipes[0];reset();struct Outcome out=quality_session(fake_init,fake_get,fake_shutdown,3072,await_real_parent,NULL);restored_state(out);check(out.acquired&&ready_calls==1);_Exit(0);}
    check(close(pipes[0])==0);check(close(pipes[1])==0);int status=0;check(waitpid(pid,&status,0)==pid&&WIFEXITED(status)&&WEXITSTATUS(status)==0);
}
int main(int argc,char **argv){
 if(argc==2){reset();transcript=true;
  if(!strcmp(argv[1],"--transcript-error")){write_fail=2;}
  else if(!strcmp(argv[1],"--transcript-conflict")){manual_key=FOVEATION;manual_value=2;}
  else if(!strcmp(argv[1],"--transcript-absent")){absent[WIDTH]=true;}
  else if(!strcmp(argv[1],"--transcript-snapshot-error")){setting_error_key=WIDTH;setting_error=6;}
  else if(!strcmp(argv[1],"--transcript-width-limit")){clamp_width=2688;}
  else check(!strcmp(argv[1],"--transcript"));
  struct Outcome out=run();print_outcome(&out);return 0;
 }
 validation();unsigned v=checks;apply_errors();unsigned a=checks-v;identity_controls();unsigned i=checks-v-a;release_controls();unsigned r=checks-v-a-i;absence_controls();unsigned n=checks-v-a-i-r;parent_eof_control();
 printf("{\"validation\":%u,\"apply_errors\":%u,\"identity\":%u,\"release\":%u,\"absence\":%u,\"parent_eof\":%u,\"checks\":%u,\"windows_or_proton_execution\":false,\"hardware_outcome\":false}\n",v,a,i,r,n,checks-v-a-i-r-n,checks);return 0;}
