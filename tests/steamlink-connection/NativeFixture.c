#define GHVR_CONNECTION_TEST
#include "../../tools/steamlink-connection-reader.c"
#include <stdlib.h>
static int checks,calls,connected_calls,class_value=1;static bool linked=true,changed=false;
static int bad, optional_missing;static char *tracking="vrlink",*model="Steam Frame",*registered="vrlink/frame";
static void check(bool value){++checks;if(!value){fprintf(stderr,"native metadata check failed%d\n",checks);exit(1);}}
static int32_t kind(uint32_t index){check(index==0);return class_value;}
static bool connected(uint32_t index){check(index==0);connected_calls++;return linked;}
static uint32_t string_property(uint32_t index,int32_t property,char *out,uint32_t cap,int32_t *error){
    check(index==0&&cap==512);calls++;*error=0;const char *value=property==1000?tracking:property==1001?model:registered;
    check(property==1000||property==1001||property==1036);
    if(optional_missing&&property!=1001){*error=4;return 0;}
    if(bad==1){*error=1;return 0;}
    if(bad==2)return cap+1;
    if(bad==3)return 0;
    if(bad==4){memset(out,'x',cap);return cap;}
    if(bad==5){memset(out,'x',120);out[120]=0;return 121;}
    if(bad==6){strcpy(out,"bad\nname");return 9;}
    if(changed&&calls>3&&property==1001)value="Quest 3";
    strcpy(out,value);return strlen(value)+1;
}
int main(void){
    struct System022 api={0};api.deviceClass=kind;api.connected=connected;api.stringProperty=string_property;struct Metadata out;
    check(read_metadata(&api,&out)==NULL);check(!strcmp(out.tracking,"vrlink")&&!strcmp(out.model,"Steam Frame"));check(calls==6&&connected_calls==2);
    linked=false;calls=0;check(!strcmp(read_metadata(&api,&out),"disconnected")&&calls==0);linked=true;
    class_value=2;check(!strcmp(read_metadata(&api,&out),"disconnected"));class_value=1;
    for(bad=1;bad<=6;bad++){calls=0;check(!strcmp(read_metadata(&api,&out),"properties"));check(calls<=3);}bad=0;
    changed=true;calls=0;check(!strcmp(read_metadata(&api,&out),"changed"));changed=false;
    optional_missing=1;calls=0;check(read_metadata(&api,&out)==NULL);check(!*out.tracking&&!*out.registered);optional_missing=0;
    api.stringProperty=NULL;check(!strcmp(read_metadata(&api,&out),"interface"));check(!strcmp(read_metadata(NULL,&out),"interface"));
    printf("{\"native_public_metadata_checks\":%d,\"actual_Windows_execution\":false}\n",checks);return 0;
}
