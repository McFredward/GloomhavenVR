#include "content_hash.h"
#include <cstdlib>
#include <cstring>
#include <limits>
#include <new>
#include <sys/stat.h>

namespace {
// SHA-256 as specified in FIPS 180-4. This small translation unit is optimized
// independently of the game's diagnostic IL2CPP code, which remains unchanged.
constexpr uint32_t constants[64] = {
    0x428a2f98,0x71374491,0xb5c0fbcf,0xe9b5dba5,0x3956c25b,0x59f111f1,0x923f82a4,0xab1c5ed5,
    0xd807aa98,0x12835b01,0x243185be,0x550c7dc3,0x72be5d74,0x80deb1fe,0x9bdc06a7,0xc19bf174,
    0xe49b69c1,0xefbe4786,0x0fc19dc6,0x240ca1cc,0x2de92c6f,0x4a7484aa,0x5cb0a9dc,0x76f988da,
    0x983e5152,0xa831c66d,0xb00327c8,0xbf597fc7,0xc6e00bf3,0xd5a79147,0x06ca6351,0x14292967,
    0x27b70a85,0x2e1b2138,0x4d2c6dfc,0x53380d13,0x650a7354,0x766a0abb,0x81c2c92e,0x92722c85,
    0xa2bfe8a1,0xa81a664b,0xc24b8b70,0xc76c51a3,0xd192e819,0xd6990624,0xf40e3585,0x106aa070,
    0x19a4c116,0x1e376c08,0x2748774c,0x34b0bcb5,0x391c0cb3,0x4ed8aa4a,0x5b9cca4f,0x682e6ff3,
    0x748f82ee,0x78a5636f,0x84c87814,0x8cc70208,0x90befffa,0xa4506ceb,0xbef9a3f7,0xc67178f2
};
struct Hash {
    uint32_t state[8] = {0x6a09e667,0xbb67ae85,0x3c6ef372,0xa54ff53a,0x510e527f,0x9b05688c,0x1f83d9ab,0x5be0cd19};
    uint8_t pending[64] = {};
    uint64_t length = 0;
    unsigned used = 0;
    bool final = false;
};
uint32_t rotate(uint32_t x, unsigned n) { return (x >> n) | (x << (32 - n)); }
void block(Hash& hash, const uint8_t* bytes) {
    uint32_t words[64];
    for (unsigned i = 0; i < 16; ++i)
        words[i] = (uint32_t(bytes[4*i]) << 24) | (uint32_t(bytes[4*i+1]) << 16) | (uint32_t(bytes[4*i+2]) << 8) | bytes[4*i+3];
    for (unsigned i = 16; i < 64; ++i) {
        uint32_t a = words[i-15], b = words[i-2];
        words[i] = words[i-16] + (rotate(a,7)^rotate(a,18)^(a>>3)) + words[i-7] + (rotate(b,17)^rotate(b,19)^(b>>10));
    }
    uint32_t a=hash.state[0],b=hash.state[1],c=hash.state[2],d=hash.state[3],e=hash.state[4],f=hash.state[5],g=hash.state[6],h=hash.state[7];
    for (unsigned i = 0; i < 64; ++i) {
        uint32_t first = h + (rotate(e,6)^rotate(e,11)^rotate(e,25)) + ((e&f)^((~e)&g)) + constants[i] + words[i];
        uint32_t second = (rotate(a,2)^rotate(a,13)^rotate(a,22)) + ((a&b)^(a&c)^(b&c));
        h=g;g=f;f=e;e=d+first;d=c;c=b;b=a;a=first+second;
    }
    hash.state[0]+=a;hash.state[1]+=b;hash.state[2]+=c;hash.state[3]+=d;
    hash.state[4]+=e;hash.state[5]+=f;hash.state[6]+=g;hash.state[7]+=h;
}
void append(Hash& hash, const uint8_t* bytes, unsigned count) {
    if (hash.used) {
        unsigned take = 64 - hash.used;
        if (take > count) take = count;
        std::memcpy(hash.pending + hash.used, bytes, take);
        hash.used += take; bytes += take; count -= take;
        if (hash.used == 64) { block(hash, hash.pending); hash.used = 0; }
    }
    while (count >= 64) { block(hash, bytes); bytes += 64; count -= 64; }
    if (count) { std::memcpy(hash.pending, bytes, count); hash.used = count; }
}
}

int ghvr_content_hash_abi() { return 1; }
void* ghvr_content_hash_create() { return new (std::nothrow) Hash(); }
int ghvr_content_hash_update(void* context, const uint8_t* bytes, int count) {
    auto* hash = static_cast<Hash*>(context);
    if (!hash || hash->final || count < 0 || (count && !bytes)
        || uint64_t(count) > (std::numeric_limits<uint64_t>::max()/8) - hash->length) return 0;
    if (count) append(*hash, bytes, unsigned(count));
    hash->length += unsigned(count);
    return 1;
}
int ghvr_content_hash_final(void* context, uint8_t* digest) {
    auto* hash = static_cast<Hash*>(context);
    if (!hash || hash->final || !digest) return 0;
    uint64_t bits = hash->length * 8;
    uint8_t padding[128] = {0x80};
    unsigned count = hash->used < 56 ? 56 - hash->used : 120 - hash->used;
    for (unsigned i = 0; i < 8; ++i) padding[count+i] = uint8_t(bits >> (56 - 8*i));
    append(*hash, padding, count+8);
    for (unsigned i = 0; i < 8; ++i)
        for (unsigned j = 0; j < 4; ++j) digest[4*i+j] = uint8_t(hash->state[i] >> (24-8*j));
    hash->final = true;
    return 1;
}
void ghvr_content_hash_destroy(void* context) { delete static_cast<Hash*>(context); }
int ghvr_content_file_identity(const char* path, GhvrContentFileIdentity* identity) {
    struct stat status;
    if (!path || !identity || lstat(path, &status) || !S_ISREG(status.st_mode)) return 0;
    identity->device = status.st_dev; identity->inode = status.st_ino; identity->size = status.st_size;
#if defined(__APPLE__)
    identity->modified_seconds = status.st_mtimespec.tv_sec; identity->modified_nanoseconds = status.st_mtimespec.tv_nsec;
    identity->changed_seconds = status.st_ctimespec.tv_sec; identity->changed_nanoseconds = status.st_ctimespec.tv_nsec;
#else
    identity->modified_seconds = status.st_mtim.tv_sec; identity->modified_nanoseconds = status.st_mtim.tv_nsec;
    identity->changed_seconds = status.st_ctim.tv_sec; identity->changed_nanoseconds = status.st_ctim.tv_nsec;
#endif
    return 1;
}
