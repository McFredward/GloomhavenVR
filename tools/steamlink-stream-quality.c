/* Temporary Steam Link stream-quality lease through public IVRSettings_003.
 * Windows/Proton child only: no OpenVR lifetime runs inside Unity/OpenXR.
 * The supported host driver reads these typed keys live on public event872.
 * It encodes RGB/coverage atlas regions at different quality when foveated;
 * uniform encoding and disabled CAS avoid that quality split without changing
 * the frozen producer/receiver, real partial opacity or additive light.
 * A setting readback is NOT proof of the actual encoder/client dimensions.
 */
#include <stdbool.h>
#include <stdint.h>
#include <stddef.h>
#include <stdio.h>
#include <string.h>
#ifndef GHVR_QUALITY_TEST
#include <windows.h>
#include <wchar.h>
#define TABLE_CALL __stdcall
#define EXPORT_CALL __cdecl
#else
#define TABLE_CALL
#define EXPORT_CALL
#endif
#define STRING_CAP 512
#define TEXT_CAP 96
#define QUALITY_SCHEMA "ghvr-steamlink-quality-v1"
static const char *const section="driver_vrlink";
enum QualityKey { WIDTH, AUTOMATIC, SHARPENING, FOVEATION, KEY_COUNT };
static const int apply_order[KEY_COUNT]={AUTOMATIC,WIDTH,SHARPENING,FOVEATION};
static const char *const keys[KEY_COUNT]={"nonFoveatedStreamFormatWidth","automaticStreamFormatWidth","useSharpening","foveationMode"};
struct System022 {
    void *recommended,*projection,*raw,*distortion,*eye,*vsync,*d3d9,*dxgi,*output;
    void *desktop,*visibility,*poses,*seated,*standing,*sorted,*activity,*transform,*role,*roleFor;
    int32_t (TABLE_CALL *deviceClass)(uint32_t);
    bool (TABLE_CALL *connected)(uint32_t);
    void *booleanProperty,*floatProperty,*intProperty,*uintProperty,*matrixProperty,*arrayProperty;
    uint32_t (TABLE_CALL *stringProperty)(uint32_t,int32_t,char *,uint32_t,int32_t *);
};
struct Settings003 {
    const char *(TABLE_CALL *errorName)(int32_t);
    void (TABLE_CALL *setBool)(const char *,const char *,bool,int32_t *);
    void (TABLE_CALL *setInt)(const char *,const char *,int32_t,int32_t *);
    void *setFloat,*setString;
    bool (TABLE_CALL *getBool)(const char *,const char *,int32_t *);
    int32_t (TABLE_CALL *getInt)(const char *,const char *,int32_t *);
    void *getFloat,*getString,*removeSection;
    void (TABLE_CALL *removeKey)(const char *,const char *,int32_t *);
};
_Static_assert(offsetof(struct System022,deviceClass)==19*sizeof(void*),"public022 class slot");
_Static_assert(offsetof(struct System022,connected)==20*sizeof(void*),"public022 connection slot");
_Static_assert(offsetof(struct System022,stringProperty)==27*sizeof(void*),"public022 string slot");
_Static_assert(offsetof(struct Settings003,setBool)==1*sizeof(void*),"public003 SetBool slot");
_Static_assert(offsetof(struct Settings003,setInt)==2*sizeof(void*),"public003 SetInt32 slot");
_Static_assert(offsetof(struct Settings003,getBool)==5*sizeof(void*),"public003 GetBool slot");
_Static_assert(offsetof(struct Settings003,getInt)==6*sizeof(void*),"public003 GetInt32 slot");
_Static_assert(offsetof(struct Settings003,removeKey)==10*sizeof(void*),"public003 RemoveKeyInSection slot");
struct Identity { char tracking[STRING_CAP],model[STRING_CAP],registered[STRING_CAP]; };
struct Lease { int32_t before[KEY_COUNT],applied[KEY_COUNT];bool attempted[KEY_COUNT],absent[KEY_COUNT];int32_t requested; };
struct Outcome { const char *error;bool acquired,restored;unsigned conflicts,errors;int key;int32_t api_error; };
typedef uint32_t (EXPORT_CALL *Init)(int32_t *,int32_t,const char *);
typedef void *(EXPORT_CALL *Get)(const char *,int32_t *);
typedef void (EXPORT_CALL *Shutdown)(void);
typedef const char *(*AwaitClose)(const struct Lease *,void *);
static bool equal_ci(const char *a,const char *b) {
    for(;;++a,++b){unsigned x=(unsigned char)*a,y=(unsigned char)*b;
        if(x>='A'&&x<='Z')x+='a'-'A';
        if(y>='A'&&y<='Z')y+='a'-'A';
        if(x!=y)return false;
        if(!x)return true;}
}
static bool registered_link(const char *s){
    if(equal_ci(s,"vrlink"))return true;
    char prefix[8];if(strlen(s)<7)return false;memcpy(prefix,s,7);prefix[7]=0;return equal_ci(prefix,"vrlink/");
}
static bool supported_identity(const struct Identity *id){
    bool registered=registered_link(id->registered);
    return (equal_ci(id->model,"Steam Frame")&&(equal_ci(id->tracking,"vrlink")||registered))
        ||(equal_ci(id->model,"Deckard MP")&&equal_ci(id->tracking,"cv")&&(!id->registered[0]||registered));
}
static bool property(struct System022 *api,int32_t id,char *out,bool optional){
    int32_t error=0;memset(out,0xff,STRING_CAP);
    uint32_t size=api->stringProperty(0,id,out,STRING_CAP,&error);
    if(optional&&error==4){out[0]=0;return true;}
    if(error||size<(optional?1u:2u)||size>STRING_CAP||out[size-1]!=0||strlen(out)!=size-1||size-1>TEXT_CAP)return false;
    for(uint32_t i=0;i<size-1;++i)if((unsigned char)out[i]<32||(unsigned char)out[i]>126)return false;
    return true;
}
static bool identity(struct System022 *api,struct Identity *out){
    memset(out,0,sizeof(*out));
    return api&&api->connected&&api->deviceClass&&api->stringProperty
        &&api->connected(0)&&api->deviceClass(0)==1
        &&property(api,1000,out->tracking,true)&&property(api,1001,out->model,false)
        &&property(api,1036,out->registered,true)&&supported_identity(out);
}
static bool same_identity(struct System022 *api,const struct Identity *before){
    struct Identity current;
    return identity(api,&current)&&!strcmp(before->tracking,current.tracking)
        &&!strcmp(before->model,current.model)&&!strcmp(before->registered,current.registered);
}
static bool valid_width(int32_t width){return width>=64&&width<=16384&&width%64==0;}
static int32_t setting_read_status(struct Settings003 *api,int key,int32_t *out){
    int32_t error=0;
    *out=(key==AUTOMATIC||key==SHARPENING)?(int32_t)api->getBool(section,keys[key],&error):api->getInt(section,keys[key],&error);
    return error;
}
static bool setting_read(struct Settings003 *api,int key,int32_t *out){return setting_read_status(api,key,out)==0;}
static bool setting_write(struct Settings003 *api,int key,int32_t value){
    int32_t error=0;
    if(key==AUTOMATIC||key==SHARPENING)api->setBool(section,keys[key],value!=0,&error);
    else api->setInt(section,keys[key],value,&error);
    return error==0;
}
static void restore(struct Settings003 *api,const struct Lease *lease,struct Outcome *out){
    /* Reverse apply order. Compare immediately before each restore; settings
     * have no public atomic compare/exchange. Never overwrite a seen manual edit.
     * A later unseen driver clamp is a conflict, not guessed ownership. */
    for(int step=KEY_COUNT-1;step>=0;--step){
        int key=apply_order[step];
        if(!lease->attempted[key])continue;
        int32_t current=0,error=setting_read_status(api,key,&current);
        if(error==5){if(!lease->absent[key])out->conflicts++;continue;}
        if(error){out->errors++;continue;}
        if(!lease->absent[key]&&current==lease->before[key])continue;
        if(current!=lease->applied[key]){out->conflicts++;continue;}
        bool written;
        if(lease->absent[key]){api->removeKey(section,keys[key],&error);written=error==0;}
        else written=setting_write(api,key,lease->before[key]);
        int32_t after=0;error=setting_read_status(api,key,&after);
        if(!written||(lease->absent[key]?error!=5:error!=0||after!=lease->before[key]))out->errors++;
    }
    out->restored=out->errors==0;
}
static struct Outcome quality_session(Init init,Get get,Shutdown shutdown,int32_t requested,AwaitClose await_close,void *context){
    struct Outcome out={0};struct Lease lease={0};lease.requested=requested;
    if(!valid_width(requested)){out.error="width";return out;}
    if(!init||!get||!shutdown||!await_close){out.error="exports";return out;}
    int32_t error=0;init(&error,3,NULL); /* Public Background never starts/keeps SteamVR alive. */
    if(error){out.error=error==121?"not_running":"initialization";return out;}
    struct System022 *system=get("FnTable:IVRSystem_022",&error);
    if(error||!system){out.error="system_interface";goto done;}
    struct Identity original;
    if(!identity(system,&original)||!same_identity(system,&original)){out.error="identity";goto done;}
    struct Settings003 *api=get("FnTable:IVRSettings_003",&error);
    if(error||!api||!api->getBool||!api->getInt||!api->setBool||!api->setInt||!api->removeKey){out.error="settings_interface";goto done;}
    /* ALL four typed snapshots precede ANY write. Error5 explicitly means
     * no setting and no default, common for the unused nonfoveated width.
     * Preserve that absence and later RemoveKeyInSection; never invent an
     * effective default. All other errors still refuse without any write. */
    for(int key=0;key<KEY_COUNT;++key){
        int32_t status=setting_read_status(api,key,&lease.before[key]);
        if(status==5){lease.absent[key]=true;lease.before[key]=0;}
        else if(status){out.error="snapshot";out.key=key;out.api_error=status;goto done;}
        lease.applied[key]=lease.before[key];
    }
    if(lease.before[FOVEATION]<0||lease.before[FOVEATION]>3){out.error="mode";goto done;}
    if(lease.before[WIDTH]<0||lease.before[WIDTH]>16384){out.error="snapshot_width";goto done;}
    if(!same_identity(system,&original)){out.error="identity_changed";goto done;}
    int32_t wanted[KEY_COUNT]={requested,0,0,1};
    if(!lease.absent[AUTOMATIC]&&!lease.absent[FOVEATION]&&!lease.absent[WIDTH]
        &&!lease.before[AUTOMATIC]&&lease.before[FOVEATION]!=0&&lease.before[FOVEATION]!=2
        &&lease.before[WIDTH]>requested)wanted[WIDTH]=lease.before[WIDTH];
    /* Disable automatic sizing before supplying a manual width; otherwise a
     * live driver settings event could immediately replace the requested width.
     * Enable nonfoveated encoding last, after its complete policy is ready. */
    for(int step=0;step<KEY_COUNT;++step){
        int key=apply_order[step];
        if(!lease.absent[key]&&wanted[key]==lease.before[key])continue;
        int32_t current=0;
        if(!same_identity(system,&original)){out.error="identity_changed";goto rollback;}
        int32_t status=setting_read_status(api,key,&current);
        if(lease.absent[key]?status!=5:status!=0||current!=lease.before[key]){out.error="changed_during_apply";goto rollback;}
        lease.attempted[key]=true;lease.applied[key]=wanted[key];
        bool written=setting_write(api,key,wanted[key]);
        int32_t after=0;bool read=setting_read(api,key,&after);
        if(!written){out.error="write";goto rollback;}
        if(!read){out.error="readback";goto rollback;}
        /* The driver can clamp width to its encoder limit and write it back.
         * Record that owned value for rollback, but do not call a smaller stream
         * an applied quality policy: MR must not silently lower the chosen size.
         * Readback still does not prove actual encoder/client dimensions. */
        if(key==WIDTH&&after!=wanted[key]&&valid_width(after)&&after<wanted[key]){
            lease.applied[key]=after;out.error="width_limited";goto rollback;
        }
        else if(after!=wanted[key]){out.error="readback";goto rollback;}
    }
    for(int key=0;key<KEY_COUNT;++key){
        int32_t current=0;
        if(!setting_read(api,key,&current)||current!=lease.applied[key]){out.error="changed_during_apply";goto rollback;}
    }
    if(!same_identity(system,&original)){out.error="identity_changed";goto rollback;}
    out.acquired=true;
    out.error=await_close(&lease,context); /* Exactly until the owning stdin closes; no expiry. */
rollback:
    restore(api,&lease,&out);
done:
    shutdown();return out;
}
static const char *print_acquired(const struct Lease *lease){
    unsigned absent=0;for(int key=0;key<KEY_COUNT;++key)if(lease->absent[key])absent|=1u<<key;
    printf("{\"schema\":\"" QUALITY_SCHEMA "\",\"event\":\"acquired\",\"applied\":true,\"requested_width\":%d,\"applied_width\":%d,\"previous_foveation_mode\":%d,\"previous_sharpening\":%s,\"previous_automatic_width\":%s,\"previous_uniform_width\":%d,\"previous_absent_mask\":%u}\n",(int)lease->requested,(int)lease->applied[WIDTH],(int)lease->before[FOVEATION],lease->before[SHARPENING]?"true":"false",lease->before[AUTOMATIC]?"true":"false",(int)lease->before[WIDTH],absent);
    return fflush(stdout)?"stdout":NULL;
}
static void print_outcome(const struct Outcome *out){
    char stage[48];const char *reason=out->error;
    if(reason&&!strcmp(reason,"snapshot")){
        static const char *const names[KEY_COUNT]={"width","automatic","sharpening","foveation"};
        snprintf(stage,sizeof(stage),"snapshot_%s_e%d",names[out->key],(int)out->api_error);reason=stage;
    }
    if(reason)printf("{\"schema\":\"" QUALITY_SCHEMA "\",\"event\":\"error\",\"error\":\"%s\",\"restored\":%s,\"conflicts\":%u,\"errors\":%u}\n",reason,(out->restored||(!out->acquired&&out->errors==0))?"true":"false",out->conflicts,out->errors);
    else printf("{\"schema\":\"" QUALITY_SCHEMA "\",\"event\":\"restored\",\"ok\":%s,\"conflicts\":%u,\"errors\":%u}\n",out->restored?"true":"false",out->conflicts,out->errors);
}
static const char *await_stdin(const struct Lease *lease,void *unused){
    FILE *input=unused?(FILE *)unused:stdin;
    const char *error=print_acquired(lease);if(error)return error;
    int byte=fgetc(input);if(byte!=EOF)return "stdin_data";
    return ferror(input)?"stdin_error":NULL;
}
#ifndef GHVR_QUALITY_TEST
/* Same bounded, no-reparse DLL loading contract as connection-reader. Holding
 * a read-sharing handle prevents replacement while this public API lease lives. */
static HANDLE checked_library(const wchar_t *path){
    size_t count=wcslen(path);if(count<4||count>=1024||!((path[0]>=L'A'&&path[0]<=L'Z')||(path[0]>=L'a'&&path[0]<=L'z'))||path[1]!=L':'||path[2]!=L'\\')return INVALID_HANDLE_VALUE;
    wchar_t canonical[1024];DWORD n=GetFullPathNameW(path,1024,canonical,NULL);
    if(!n||n>=1024||_wcsicmp(path,canonical))return INVALID_HANDLE_VALUE;
    if(_wcsicmp(wcsrchr(path,L'\\')+1,L"openvr_api.dll"))return INVALID_HANDLE_VALUE;
    for(size_t i=3;i<count;++i){if(path[i]!=L'\\')continue;wchar_t c=canonical[i];canonical[i]=0;DWORD flags=GetFileAttributesW(canonical);canonical[i]=c;if(flags==INVALID_FILE_ATTRIBUTES||(flags&FILE_ATTRIBUTE_REPARSE_POINT))return INVALID_HANDLE_VALUE;}
    DWORD flags=GetFileAttributesW(path);if(flags==INVALID_FILE_ATTRIBUTES||(flags&(FILE_ATTRIBUTE_REPARSE_POINT|FILE_ATTRIBUTE_DIRECTORY)))return INVALID_HANDLE_VALUE;
    HANDLE h=CreateFileW(path,GENERIC_READ,FILE_SHARE_READ,NULL,OPEN_EXISTING,FILE_ATTRIBUTE_NORMAL,NULL);if(h==INVALID_HANDLE_VALUE)return h;
    LARGE_INTEGER size;IMAGE_DOS_HEADER dos;IMAGE_NT_HEADERS64 pe;DWORD bytes;
    bool ok=GetFileSizeEx(h,&size)&&size.QuadPart>=512&&size.QuadPart<=8*1024*1024&&ReadFile(h,&dos,sizeof(dos),&bytes,NULL)&&bytes==sizeof(dos)&&dos.e_magic==IMAGE_DOS_SIGNATURE&&dos.e_lfanew>=64&&dos.e_lfanew<=size.QuadPart-(LONGLONG)sizeof(pe);
    if(ok)ok=SetFilePointer(h,dos.e_lfanew,NULL,FILE_BEGIN)!=INVALID_SET_FILE_POINTER&&ReadFile(h,&pe,sizeof(pe),&bytes,NULL)&&bytes==sizeof(pe)&&pe.Signature==IMAGE_NT_SIGNATURE&&pe.FileHeader.Machine==IMAGE_FILE_MACHINE_AMD64&&pe.FileHeader.SizeOfOptionalHeader>=sizeof(IMAGE_OPTIONAL_HEADER64)&&pe.OptionalHeader.Magic==IMAGE_NT_OPTIONAL_HDR64_MAGIC&&(pe.FileHeader.Characteristics&IMAGE_FILE_DLL);
    if(!ok){CloseHandle(h);return INVALID_HANDLE_VALUE;}return h;
}
static int failure(const char *reason){
    /* Abandoned ownership means a prior lease may have lost restoration. Do
     * not report a safe baseline merely because this new process wrote nothing. */
    printf("{\"schema\":\"" QUALITY_SCHEMA "\",\"event\":\"error\",\"error\":\"%s\",\"restored\":%s,\"conflicts\":0,\"errors\":0}\n",reason,!strcmp(reason,"abandoned")?"false":"true");return 2;
}
static bool parse_width(const wchar_t *s,int32_t *width){
    int32_t n=0;if(!*s)return false;
    for(;*s;++s){if(*s<L'0'||*s>L'9'||n>16384)return false;n=n*10+(*s-L'0');}
    if(!valid_width(n))return false;
    *width=n;return true;
}
int wmain(int argc,wchar_t **argv){
    SetErrorMode(SEM_FAILCRITICALERRORS|SEM_NOGPFAULTERRORBOX|SEM_NOOPENFILEERRORBOX);SetDllDirectoryW(L"");
    int32_t width=0;if(argc!=3||!parse_width(argv[2],&width))return failure("arguments");
    if(GetFileType(GetStdHandle(STD_INPUT_HANDLE))!=FILE_TYPE_PIPE)return failure("stdin_pipe");
    HANDLE mutex=CreateMutexW(NULL,FALSE,L"Local\\GloomhavenVR.SteamLink.StreamQuality.v1");
    if(!mutex)return failure("mutex");
    DWORD lock=WaitForSingleObject(mutex,0);
    if(lock!=WAIT_OBJECT_0){
        if(lock==WAIT_ABANDONED)ReleaseMutex(mutex);
        CloseHandle(mutex);return failure(lock==WAIT_TIMEOUT?"busy":lock==WAIT_ABANDONED?"abandoned":"mutex");
    }
    HANDLE file=checked_library(argv[1]);if(file==INVALID_HANDLE_VALUE){ReleaseMutex(mutex);CloseHandle(mutex);return failure("library");}
    HMODULE library=LoadLibraryExW(argv[1],NULL,LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR|LOAD_LIBRARY_SEARCH_SYSTEM32);
    if(!library){CloseHandle(file);ReleaseMutex(mutex);CloseHandle(mutex);return failure("library");}
    Init init=NULL;Get get=NULL;Shutdown shutdown=NULL;FARPROC address=GetProcAddress(library,"VR_InitInternal2");memcpy(&init,&address,sizeof(init));
    address=GetProcAddress(library,"VR_GetGenericInterface");memcpy(&get,&address,sizeof(get));
    address=GetProcAddress(library,"VR_ShutdownInternal");memcpy(&shutdown,&address,sizeof(shutdown));
    struct Outcome out=quality_session(init,get,shutdown,width,await_stdin,NULL);
    FreeLibrary(library);CloseHandle(file);ReleaseMutex(mutex);CloseHandle(mutex);
    print_outcome(&out);
    return out.error||out.errors?2:0;
}
#endif
