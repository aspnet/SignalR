@Library('dotnet-ci') _

// 'node' indicates to Jenkins that the enclosed block runs on a node that matches
// the label 'windows-with-vs'
simpleNode('Windows.10.Enterprise.RS3.ASPNET') {
    stage ('Checking out source') {
        checkout scm
    }
    stage ('Build') {
        def logDir = "${WORKSPACE}/logs"
        def environment = "set ASPNETCORE_TEST_LOG_DIR=${logDir}"
        bat "${environment} & .\\run.cmd -CI default-build"
        archiveArtifacts allowEmptyArchive: false, artifacts: "${logDir}/*.log"
    }
}
