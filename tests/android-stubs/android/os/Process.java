package android.os;
public final class Process {
 public static final int THREAD_PRIORITY_AUDIO=-16;
 public static volatile boolean fail;
 public static void setThreadPriority(int priority){if(fail)throw new SecurityException("test priority failure");}
}
