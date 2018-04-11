@Library('dotnet-ci') _

// 'node' indicates to Jenkins that the enclosed block runs on a node that matches
// the label 'windows-with-vs'
simpleNode('Windows.10.Enterprise.RS3.ASPNET') {
    stage ('Checking out source') {
        checkout scm
    }
    def logDir = "${WORKSPACE}/testlogs"
    try{
        stage ('Build') {
            def environment = "set ASPNETCORE_TEST_LOG_DIR=${logDir}"
            bat "${environment} & set ASPNETCORE_TEST_LOG_DIR= .\\run.cmd -CI default-build"
        }
    }
    finally {
        archiveArtifacts allowEmptyArchive: false, artifacts: "${logDir}/**"
    }
}
