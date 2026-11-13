package dev.gloomhavenvr.tools;

import java.io.ByteArrayOutputStream;
import java.io.InputStream;
import java.io.PrintStream;
import java.lang.reflect.InvocationTargetException;
import java.lang.reflect.Method;
import java.nio.ByteBuffer;
import java.nio.charset.CharacterCodingException;
import java.nio.charset.CodingErrorAction;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.LinkOption;
import java.nio.file.Path;
import java.nio.file.Paths;
import java.nio.file.attribute.BasicFileAttributes;
import java.util.ArrayList;
import java.util.HashSet;
import java.util.List;
import java.util.Set;

/**
 * Shell-only platform metadata repair for the two generated Quest asset banks.
 *
 * This is deliberately independent of the game APK, run-as, JDWP and native
 * debugger attachment. The host owns installed-APK/hardware identity checks,
 * stops the game, stages this tiny DEX under its own /data/local/tmp directory,
 * and verifies the resulting metadata. No game or save payload is read here.
 *
 * AOSP Android 14 StorageManagerService.fixupAppDir resolves the installed
 * package UID and asks vold to repair the underlying filesystem. A plain chmod
 * through MediaProvider FUSE can be a no-op. Do not replace this call with a
 * guessed Binder transaction number or disable application storage isolation.
 *
 * Important platform boundary: vold normalizes each requested path ancestor
 * and its immediate children, including package/files-root siblings. It does
 * not recursively read or copy unknown saved data. Those ownership/mode/quota
 * changes are platform-managed app-directory metadata, not an assertion that
 * the actual app can read its assets. A returned call is NOT an app-open proof;
 * Android can log a vold failure without propagating it through this method.
 */
public final class StorageAccess {
    static final String MAGIC = "GHVRQ_STORAGE_FIXUP_V1";
    static final String END = "END";
    static final String BASE = "/storage/emulated/0/Android/data/dev.gloomhavenvr.quest/files/";
    static final String[] BANKS = {BASE + "quest-mod-resources", BASE + "quest-owned-game"};
    static final int MAX_LINE_BYTES = 8192;
    static final int MAX_INPUT_BYTES = 4 * 1024 * 1024;
    static final int MAX_DIRECTORIES = 32768;

    interface DirectoryFacts {
        void requireOrdinaryDirectory(String path) throws Exception;
    }

    private static final class RealDirectories implements DirectoryFacts {
        @Override
        public void requireOrdinaryDirectory(String value) throws Exception {
            Path path = Paths.get(value);
            Path current = path.getRoot();
            for (Path component : path) {
                current = current.resolve(component);
                BasicFileAttributes attributes = Files.readAttributes(current,
                        BasicFileAttributes.class, LinkOption.NOFOLLOW_LINKS);
                if (!attributes.isDirectory() || attributes.isSymbolicLink()) {
                    throw new IllegalArgumentException("Storage path has a non-directory or symbolic-link component.");
                }
            }
            if (!path.toFile().getCanonicalPath().equals(value)) {
                throw new IllegalArgumentException("Storage path does not retain its exact canonical boundary.");
            }
        }
    }

    private StorageAccess() { }

    public static void main(String[] arguments) {
        try {
            if (arguments.length != 0) throw new IllegalArgumentException("This helper accepts only its bounded stdin protocol.");
            execute(System.in, System.out, new RealDirectories());
        } catch (Exception error) {
            Throwable detail = error;
            while (detail instanceof InvocationTargetException && detail.getCause() != null) {
                detail = detail.getCause();
            }
            String message = detail.getClass().getSimpleName() + ": " + String.valueOf(detail.getMessage());
            message = message.replace('\n', ' ').replace('\r', ' ').replace('\t', ' ');
            if (message.length() > 768) message = message.substring(0, 768);
            System.err.println("GHVRQ_STORAGE_ERROR\t" + message);
            System.exit(2);
        }
    }

    // Package-private to exercise the actual protocol and Android reflection on
    // a JVM with explicit filesystem/Binder stand-ins. The production entry point
    // always supplies RealDirectories and exposes no test or arbitrary-path flag.
    static void execute(InputStream input, PrintStream output, DirectoryFacts facts) throws Exception {
        int uid = ((Integer) Class.forName("android.os.Process").getMethod("myUid").invoke(null)).intValue();
        int sdk = Class.forName("android.os.Build$VERSION").getField("SDK_INT").getInt(null);
        if (uid != 2000) throw new IllegalStateException("Storage repair requires the primary-user Android shell UID 2000.");
        if (sdk < 30) throw new IllegalStateException("Storage repair requires Android 11 or newer.");

        LimitedLines lines = new LimitedLines(input);
        if (!MAGIC.equals(lines.read())) throw new IllegalArgumentException("Invalid storage protocol header.");
        List<String> directories = new ArrayList<String>();
        Set<String> seen = new HashSet<String>();
        while (true) {
            String path = lines.read();
            if (path == null) throw new IllegalArgumentException("Incomplete storage protocol.");
            if (END.equals(path)) break;
            validatePath(path);
            if (directories.size() >= MAX_DIRECTORIES || !seen.add(path)) {
                throw new IllegalArgumentException("Excessive or repeated storage directory.");
            }
            directories.add(path);
        }
        if (directories.isEmpty() || lines.read() != null) {
            throw new IllegalArgumentException("Empty storage request or unexpected trailing data.");
        }

        // Complete input and every path are qualified before the first platform
        // mutation. Recheck the current path before and after each request too;
        // the host preserves its force-stop and exact hardware/source guard.
        for (String path : directories) facts.requireOrdinaryDirectory(path);

        Class<?> binderType = Class.forName("android.os.IBinder");
        Object binder = Class.forName("android.os.ServiceManager")
                .getMethod("getService", String.class).invoke(null, "mount");
        if (binder == null) throw new IllegalStateException("Android storage service is unavailable.");
        Object storage = Class.forName("android.os.storage.IStorageManager$Stub")
                .getMethod("asInterface", binderType).invoke(null, binder);
        if (storage == null) throw new IllegalStateException("Android storage service interface is unavailable.");
        Method fixup = Class.forName("android.os.storage.IStorageManager")
                .getMethod("fixupAppDir", String.class);

        output.println("GHVRQ_STORAGE_BEGIN\t" + directories.size());
        output.flush();
        int completed = 0;
        for (String path : directories) {
            facts.requireOrdinaryDirectory(path);
            fixup.invoke(storage, path);
            facts.requireOrdinaryDirectory(path);
            output.println("GHVRQ_STORAGE_DONE\t" + (++completed) + "\t" + path);
            output.flush();
        }
        output.println("GHVRQ_STORAGE_COMPLETE\t" + completed);
        output.flush();
    }

    static void validatePath(String path) {
        if (path == null || path.length() == 0 || path.indexOf('\0') >= 0 || path.indexOf('\r') >= 0
                || path.indexOf('\n') >= 0 || path.indexOf('\t') >= 0 || path.indexOf('\\') >= 0
                || path.getBytes(StandardCharsets.UTF_8).length > MAX_LINE_BYTES) {
            throw new IllegalArgumentException("Invalid storage path framing.");
        }
        for (String component : path.substring(1).split("/", -1)) {
            if (component.length() == 0 || ".".equals(component) || "..".equals(component)) {
                throw new IllegalArgumentException("Storage path has an empty or relative component.");
            }
        }
        for (String bank : BANKS) {
            if (path.equals(bank) || path.equals(bank + "/StreamingAssets")
                    || path.startsWith(bank + "/StreamingAssets/")) return;
        }
        throw new IllegalArgumentException("Storage path is outside this app's generated asset banks.");
    }

    private static final class LimitedLines {
        private final InputStream input;
        private int total;

        LimitedLines(InputStream input) { this.input = input; }

        String read() throws Exception {
            ByteArrayOutputStream bytes = new ByteArrayOutputStream();
            while (true) {
                int value = input.read();
                if (value < 0) {
                    if (bytes.size() != 0) throw new IllegalArgumentException("Storage protocol needs LF-terminated lines.");
                    return null;
                }
                if (++total > MAX_INPUT_BYTES) throw new IllegalArgumentException("Storage protocol exceeds its byte limit.");
                if (value == '\n') break;
                if (bytes.size() >= MAX_LINE_BYTES) throw new IllegalArgumentException("Storage protocol line exceeds its byte limit.");
                bytes.write(value);
            }
            try {
                return StandardCharsets.UTF_8.newDecoder()
                        .onMalformedInput(CodingErrorAction.REPORT)
                        .onUnmappableCharacter(CodingErrorAction.REPORT)
                        .decode(ByteBuffer.wrap(bytes.toByteArray())).toString();
            } catch (CharacterCodingException error) {
                throw new IllegalArgumentException("Storage protocol is not valid UTF-8.");
            }
        }
    }
}
