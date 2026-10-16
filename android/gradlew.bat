@rem PCam launcher for the Gradle Wrapper 8.9 JAR (Apache-2.0).
@rem Runtime source: https://github.com/gradle/gradle/tree/v8.9.0 ; see ../THIRD-PARTY.md.
@echo off
setlocal
if defined JAVA_HOME (
  set "JAVA_EXE=%JAVA_HOME%\bin\java.exe"
) else (
  set "JAVA_EXE=java.exe"
)
"%JAVA_EXE%" %JAVA_OPTS% %GRADLE_OPTS% -classpath "%~dp0gradle\wrapper\gradle-wrapper.jar" org.gradle.wrapper.GradleWrapperMain %*
exit /b %ERRORLEVEL%

