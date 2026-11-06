plugins {
    kotlin("jvm")
}

dependencies {
    api(project(":simplebehav-dsl"))
    implementation("com.microsoft.playwright:playwright:1.49.0")
    compileOnly("org.junit.jupiter:junit-jupiter-api:5.11.4")
    testImplementation("org.junit.jupiter:junit-jupiter:5.11.4")
    testImplementation("org.jetbrains.kotlinx:kotlinx-coroutines-core:1.10.1")
    testRuntimeOnly("org.junit.platform:junit-platform-launcher")
}

tasks.named<Test>("test") {
    // Browser tests need Playwright's Chromium. `gradle test` does not run them.
    useJUnitPlatform {
        excludeTags("browser")
    }
}

tasks.register<Test>("browserTest") {
    testClassesDirs = sourceSets["test"].output.classesDirs
    classpath = sourceSets["test"].runtimeClasspath
    useJUnitPlatform {
        includeTags("browser")
    }
    shouldRunAfter(tasks.named("test"))
}
