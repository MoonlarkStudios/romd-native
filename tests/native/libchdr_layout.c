/* Measure the actual pinned ABI using the host compiler and platform headers. */
#include <stddef.h>
#include <stdint.h>
#include <stdio.h>
#include <libchdr/chd.h>

#define TYPE(name, type, suffix) \
    printf("\"" name "\":{\"size\":%zu,\"alignment\":%zu}" suffix, sizeof(type), _Alignof(type))
#define BEGIN(type) \
    printf("\"" #type "\":{\"size\":%zu,\"alignment\":%zu,\"fields\":{", sizeof(type), _Alignof(type))
#define FIELD(type, field, suffix) \
    printf("\"" #field "\":{\"offset\":%zu,\"size\":%zu,\"alignment\":%zu}" suffix, \
           offsetof(type, field), sizeof(((type *)0)->field), _Alignof(__typeof__(((type *)0)->field)))
#define END(suffix) printf("}}" suffix)
#define ERROR(name, suffix) printf("\"" #name "\":%u" suffix, (unsigned int)(name))

int main(void)
{
    printf("{\"schemaVersion\":1,\"platformMacros\":{");
#if defined(__APPLE__)
    printf("\"apple\":true,");
#else
    printf("\"apple\":false,");
#endif
#if defined(__linux__)
    printf("\"linux\":true,");
#else
    printf("\"linux\":false,");
#endif
#if defined(_WIN32)
    printf("\"windows\":true},");
#else
    printf("\"windows\":false},");
#endif
    printf("\"architectureMacros\":{");
#if defined(__aarch64__) || defined(__arm64__) || defined(_M_ARM64)
    printf("\"arm64\":true,");
#else
    printf("\"arm64\":false,");
#endif
#if defined(__x86_64__) || defined(_M_X64)
    printf("\"x64\":true},");
#else
    printf("\"x64\":false},");
#endif
    printf("\"primitives\":{");
    TYPE("uint32_t", uint32_t, ",");
    TYPE("uint64_t", uint64_t, ",");
    TYPE("int64_t", int64_t, ",");
    TYPE("size_t", size_t, ",");
    TYPE("int", int, ",");
    TYPE("pointer", void *, ",");
    TYPE("chd_error", chd_error, "},\"structures\":{");

    BEGIN(chd_header);
    FIELD(chd_header, length, ",");
    FIELD(chd_header, version, ",");
    FIELD(chd_header, flags, ",");
    FIELD(chd_header, compression, ",");
    FIELD(chd_header, hunkbytes, ",");
    FIELD(chd_header, totalhunks, ",");
    FIELD(chd_header, logicalbytes, ",");
    FIELD(chd_header, metaoffset, ",");
    FIELD(chd_header, mapoffset, ",");
    FIELD(chd_header, md5, ",");
    FIELD(chd_header, parentmd5, ",");
    FIELD(chd_header, sha1, ",");
    FIELD(chd_header, rawsha1, ",");
    FIELD(chd_header, parentsha1, ",");
    FIELD(chd_header, unitbytes, ",");
    FIELD(chd_header, unitcount, ",");
    FIELD(chd_header, hunkcount, ",");
    FIELD(chd_header, mapentrybytes, ",");
    FIELD(chd_header, rawmap, ",");
    FIELD(chd_header, obsolete_cylinders, ",");
    FIELD(chd_header, obsolete_sectors, ",");
    FIELD(chd_header, obsolete_heads, ",");
    FIELD(chd_header, obsolete_hunksize, "");
    END(",");

    BEGIN(core_file_callbacks);
    FIELD(core_file_callbacks, fsize, ",");
    FIELD(core_file_callbacks, fread, ",");
    FIELD(core_file_callbacks, fclose, ",");
    FIELD(core_file_callbacks, fseek, "");
    END(",");

    BEGIN(core_file_callbacks_and_argp);
    FIELD(core_file_callbacks_and_argp, callbacks, ",");
    FIELD(core_file_callbacks_and_argp, argp, "");
    END(",");

    BEGIN(core_file);
    FIELD(core_file, argp, ",");
    FIELD(core_file, fsize, ",");
    FIELD(core_file, fread, ",");
    FIELD(core_file, fclose, ",");
    FIELD(core_file, fseek, "");
    END(",");

    BEGIN(chd_verify_result);
    FIELD(chd_verify_result, md5, ",");
    FIELD(chd_verify_result, sha1, ",");
    FIELD(chd_verify_result, rawsha1, ",");
    FIELD(chd_verify_result, metasha1, "");
    END("},\"errors\":{");

    ERROR(CHDERR_NONE, ",");
    ERROR(CHDERR_NO_INTERFACE, ",");
    ERROR(CHDERR_OUT_OF_MEMORY, ",");
    ERROR(CHDERR_INVALID_FILE, ",");
    ERROR(CHDERR_INVALID_PARAMETER, ",");
    ERROR(CHDERR_INVALID_DATA, ",");
    ERROR(CHDERR_FILE_NOT_FOUND, ",");
    ERROR(CHDERR_REQUIRES_PARENT, ",");
    ERROR(CHDERR_FILE_NOT_WRITEABLE, ",");
    ERROR(CHDERR_READ_ERROR, ",");
    ERROR(CHDERR_WRITE_ERROR, ",");
    ERROR(CHDERR_CODEC_ERROR, ",");
    ERROR(CHDERR_INVALID_PARENT, ",");
    ERROR(CHDERR_HUNK_OUT_OF_RANGE, ",");
    ERROR(CHDERR_DECOMPRESSION_ERROR, ",");
    ERROR(CHDERR_COMPRESSION_ERROR, ",");
    ERROR(CHDERR_CANT_CREATE_FILE, ",");
    ERROR(CHDERR_CANT_VERIFY, ",");
    ERROR(CHDERR_NOT_SUPPORTED, ",");
    ERROR(CHDERR_METADATA_NOT_FOUND, ",");
    ERROR(CHDERR_INVALID_METADATA_SIZE, ",");
    ERROR(CHDERR_UNSUPPORTED_VERSION, ",");
    ERROR(CHDERR_VERIFY_INCOMPLETE, ",");
    ERROR(CHDERR_INVALID_METADATA, ",");
    ERROR(CHDERR_INVALID_STATE, ",");
    ERROR(CHDERR_OPERATION_PENDING, ",");
    ERROR(CHDERR_NO_ASYNC_OPERATION, ",");
    ERROR(CHDERR_UNSUPPORTED_FORMAT, "}}\n");
    return 0;
}
