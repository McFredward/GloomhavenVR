/* Android adapters for the pinned GNU guest ABI, scoped to the private worker.
 * The worker uses C.UTF-8 and ships no gettext message catalogs. No engine/game
 * function is replaced. GNU random state/sequence semantics are tested against
 * the host GNU library independently; Android execution still needs hardware.
 */
#include <stdint.h>
#include <stddef.h>
#include <errno.h>
#include <pthread.h>

#ifndef EXPORT
#define EXPORT __attribute__((visibility("default")))
#endif
extern int *__errno(void);
extern void __assert2(const char *, int, const char *, const char *) __attribute__((noreturn));

EXPORT __attribute__((noreturn)) void my___assert_fail(const char *assertion,
        const char *file, uint32_t line, const char *function)
{
    __assert2(file, (int)line, function, assertion);
}

/* GNU uses network-order classification bits, not Bionic's _ctype_ layout.
 * Entries -128..-2 mirror unsigned bytes; EOF (-1) stays distinct. Return a
 * pointer to a thread's pointer, as the GNU accessor contract requires.
 */
static uint16_t quest_ctype_bits[384];
static int32_t quest_ctype_lower[384], quest_ctype_upper[384];
static pthread_once_t quest_ctype_once = PTHREAD_ONCE_INIT;
static __thread const uint16_t *quest_ctype_b_ptr;
static __thread const int32_t *quest_ctype_lower_ptr, *quest_ctype_upper_ptr;
static void quest_ctype_init(void)
{
    for (int c = -128; c <= 255; ++c) {
        int index = c + 128, value = c < -1 ? c + 256 : c;
        int upper = value >= 'A' && value <= 'Z';
        int lower = value >= 'a' && value <= 'z';
        int digit = value >= '0' && value <= '9';
        int alpha = upper || lower;
        int space = value == ' ' || (value >= '\t' && value <= '\r');
        int print = value >= 32 && value <= 126;
        int graph = value >= 33 && value <= 126;
        int hex = digit || (value >= 'a' && value <= 'f') || (value >= 'A' && value <= 'F');
        quest_ctype_bits[index] = (uint16_t)((upper ? 0x0100 : 0) | (lower ? 0x0200 : 0)
            | (alpha ? 0x0400 : 0) | (digit ? 0x0800 : 0) | (hex ? 0x1000 : 0)
            | (space ? 0x2000 : 0) | (print ? 0x4000 : 0) | (graph ? 0x8000 : 0)
            | (value == ' ' || value == '\t' ? 0x0001 : 0)
            | ((value >= 0 && value < 32) || value == 127 ? 0x0002 : 0)
            | (graph && !alpha && !digit ? 0x0004 : 0) | (alpha || digit ? 0x0008 : 0));
        quest_ctype_lower[index] = upper ? value + ('a' - 'A') : value;
        quest_ctype_upper[index] = lower ? value - ('a' - 'A') : value;
    }
}
EXPORT const uint16_t **my___ctype_b_loc(void)
{
    pthread_once(&quest_ctype_once, quest_ctype_init);
    quest_ctype_b_ptr = quest_ctype_bits + 128;
    return &quest_ctype_b_ptr;
}
EXPORT const int32_t **my___ctype_tolower_loc(void)
{
    pthread_once(&quest_ctype_once, quest_ctype_init);
    quest_ctype_lower_ptr = quest_ctype_lower + 128;
    return &quest_ctype_lower_ptr;
}
EXPORT const int32_t **my___ctype_toupper_loc(void)
{
    pthread_once(&quest_ctype_once, quest_ctype_init);
    quest_ctype_upper_ptr = quest_ctype_upper + 128;
    return &quest_ctype_upper_ptr;
}

EXPORT const char *my_dcgettext(const char *domain, const char *message, int category)
{
    (void)domain; (void)category;
    /* C/C.UTF-8 and no MO catalogs: return the original diagnostic string. */
    return message;
}

/* GNU x86_64 random_data is 48 bytes, with pointers at 0/8/16/40. Bionic's
 * global random() cannot stand in for this caller-owned, reentrant state.
 */
typedef struct quest_random_data {
    int32_t *fptr, *rptr, *state;
    int32_t rand_type, rand_deg, rand_sep;
    int32_t *end_ptr;
} quest_random_data;
_Static_assert(sizeof(quest_random_data) == 48, "GNU 64-bit random_data layout");
_Static_assert(offsetof(quest_random_data, end_ptr) == 40, "GNU random_data end_ptr");
static int quest_random_error(void) { *__errno() = EINVAL; return -1; }
EXPORT int my_random_r(quest_random_data *buf, int32_t *result)
{
    if (!buf || !result || !buf->state || buf->rand_type < 0 || buf->rand_type > 4)
        return quest_random_error();
    if (buf->rand_type == 0) {
        uint32_t value = ((uint32_t)buf->state[0] * 1103515245u + 12345u) & 0x7fffffffu;
        buf->state[0] = (int32_t)value;
        *result = (int32_t)value;
    } else {
        uint32_t value = (uint32_t)*buf->fptr + (uint32_t)*buf->rptr;
        *buf->fptr = (int32_t)value;
        *result = (int32_t)(value >> 1);
        if (++buf->fptr >= buf->end_ptr) {
            buf->fptr = buf->state;
            ++buf->rptr;
        } else if (++buf->rptr >= buf->end_ptr) {
            buf->rptr = buf->state;
        }
    }
    return 0;
}
EXPORT int my_initstate_r(uint32_t seed, char *arg_state, size_t length, quest_random_data *buf)
{
    static const int degrees[] = {0, 7, 15, 31, 63}, separations[] = {0, 3, 1, 3, 1};
    if (!buf || !arg_state) return quest_random_error();
    if (buf->state) {
        if (buf->rand_type < 0 || buf->rand_type > 4) return quest_random_error();
        buf->state[-1] = buf->rand_type == 0 ? 0
            : (int32_t)(5 * (buf->rptr - buf->state) + buf->rand_type);
    }
    if (length < 8) return quest_random_error();
    int type = length < 32 ? 0 : length < 64 ? 1 : length < 128 ? 2 : length < 256 ? 3 : 4;
    buf->rand_type = type; buf->rand_deg = degrees[type]; buf->rand_sep = separations[type];
    buf->state = (int32_t *)arg_state + 1;
    buf->end_ptr = buf->state + buf->rand_deg;
    if (!seed) seed = 1;
    buf->state[0] = (int32_t)seed;
    if (type) {
        /* Signed division/remainder matters for seeds >= 0x80000000. */
        int32_t word = (int32_t)seed;
        for (int i = 1; i < buf->rand_deg; ++i) {
            int32_t hi = word / 127773, lo = word % 127773;
            word = 16807 * lo - 2836 * hi;
            if (word < 0) word += 2147483647;
            buf->state[i] = word;
        }
        buf->fptr = buf->state + buf->rand_sep;
        buf->rptr = buf->state;
        for (int i = 0; i < 10 * buf->rand_deg; ++i) {
            int32_t discard;
            my_random_r(buf, &discard);
        }
    }
    buf->state[-1] = type == 0 ? 0 : (int32_t)(5 * (buf->rptr - buf->state) + type);
    return 0;
}
