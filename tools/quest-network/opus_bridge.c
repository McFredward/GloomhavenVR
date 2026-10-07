/* Fixed-argument Photon Voice CTL imports for the AArch64 variadic ABI.
 * Opus packet/PCM APIs and request numbers remain the original standard API.
 * Calling opus_*_ctl directly from a fixed managed P/Invoke happens to work
 * on x86-64, but Android ARM64 passes variadic arguments differently. */
#include <opus.h>

#define QUEST_EXPORT __attribute__((visibility("default")))
QUEST_EXPORT int quest_opus_encoder_ctl_set(OpusEncoder *state, int request, int value)
{
    return opus_encoder_ctl(state, request, value);
}
QUEST_EXPORT int quest_opus_encoder_ctl_get(OpusEncoder *state, int request, int *value)
{
    return opus_encoder_ctl(state, request, value);
}
QUEST_EXPORT int quest_opus_decoder_ctl_set(OpusDecoder *state, int request, int value)
{
    return opus_decoder_ctl(state, request, value);
}
QUEST_EXPORT int quest_opus_decoder_ctl_get(OpusDecoder *state, int request, int *value)
{
    return opus_decoder_ctl(state, request, value);
}
